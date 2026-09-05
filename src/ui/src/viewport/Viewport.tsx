import { useEffect, useRef, useState } from 'react';
import * as THREE from 'three';
import { useApp } from '../state/store';
import { Badge, Button, EmptyState, Spinner } from '../ui/primitives';
import './viewport.css';

export type ShadingMode = 'material' | 'solid' | 'wireframe' | 'normals' | 'uv';
export type CameraPreset = 'front' | 'back' | 'left' | 'right' | 'top' | 'bottom' | 'reset';

/** Decodes the host's packed vertex blob into typed arrays. */
function decodeBuffer(base64: string, layout: Record<string, [number, number]>) {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  const view = bytes.buffer;

  const slice = (name: string, Ctor: typeof Float32Array | typeof Uint32Array) => {
    const entry = layout[name];
    if (!entry) return null;
    const [offset, length] = entry;
    if (length === 0) return null;
    return new Ctor(view.slice(offset, offset + length));
  };

  return {
    positions: slice('positions', Float32Array) as Float32Array | null,
    normals: slice('normals', Float32Array) as Float32Array | null,
    uvs: slice('uvs', Float32Array) as Float32Array | null,
    indices: slice('indices', Uint32Array) as Uint32Array | null,
  };
}

/**
 * A UV checker, generated once.
 *
 * Applied in UV shading mode, this shows how the drawable's UVs are laid out on
 * the mesh itself: stretched squares mean stretched texels. It is a diagnostic
 * built from the asset's own coordinates, not decoration.
 */
function makeUvChecker(): THREE.Texture {
  const size = 512;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext('2d')!;

  const cells = 16;
  const step = size / cells;
  for (let y = 0; y < cells; y++) {
    for (let x = 0; x < cells; x++) {
      const even = (x + y) % 2 === 0;
      ctx.fillStyle = even ? '#3a4034' : '#8f9a83';
      ctx.fillRect(x * step, y * step, step, step);
    }
  }

  ctx.strokeStyle = 'rgba(168,225,12,0.85)';
  ctx.lineWidth = 2;
  ctx.strokeRect(1, 1, size - 2, size - 2);

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.flipY = false;
  return texture;
}

export function Viewport({ textureUrl, onToggleFullscreen, fullscreen }: {
  textureUrl?: string | null;
  onToggleFullscreen?: () => void;
  fullscreen?: boolean;
}) {
  const mount = useRef<HTMLDivElement>(null);
  const meshInfo = useApp((s) => s.meshInfo);
  const meshLoading = useApp((s) => s.meshLoading);
  const meshError = useApp((s) => s.meshError);
  const loadMesh = useApp((s) => s.loadMesh);
  const developerMode = useApp((s) => s.info?.developerMode ?? false);

  const [shading, setShading] = useState<ShadingMode>('material');
  const [showGrid, setShowGrid] = useState(true);
  const [showBounds, setShowBounds] = useState(false);
  const [showNormals, setShowNormals] = useState(false);
  const [orthographic, setOrthographic] = useState(false);
  const [fov, setFov] = useState(38);
  const [stats, setStats] = useState<{ fps: number } | null>(null);

  // Three.js objects live outside React state; re-rendering must never rebuild
  // the renderer.
  const engine = useRef<{
    renderer: THREE.WebGLRenderer;
    scene: THREE.Scene;
    camera: THREE.PerspectiveCamera;
    ortho: THREE.OrthographicCamera;
    active: () => THREE.Camera;
    useOrtho: boolean;
    target: THREE.Vector3;
    spherical: THREE.Spherical;
    grid: THREE.GridHelper;
    axes: THREE.AxesHelper;
    mesh?: THREE.Mesh;
    material?: THREE.MeshStandardMaterial;
    normalMaterial?: THREE.MeshNormalMaterial;
    uvMaterial?: THREE.MeshBasicMaterial;
    wire?: THREE.LineSegments;
    box?: THREE.BoxHelper;
    normalsHelper?: THREE.LineSegments;
    dispose: () => void;
    frame: () => void;
  } | null>(null);

  /* ---------------------------------------------------------------- setup */

  useEffect(() => {
    const host = mount.current;
    if (!host) return;

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.setClearColor(0x0e0f0d, 1);
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    host.appendChild(renderer.domElement);

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(38, 1, 0.01, 100);
    // Orthographic is the view to check proportions and silhouette in; both
    // cameras share one orbit state so switching never loses your place.
    const ortho = new THREE.OrthographicCamera(-1, 1, 1, -1, 0.01, 100);

    // Three-point lighting: a key from front-left, a cool fill, and a rim so
    // silhouette reads against the dark background.
    scene.add(new THREE.AmbientLight(0xffffff, 0.55));
    const key = new THREE.DirectionalLight(0xfff4e0, 2.1);
    key.position.set(2.4, 3.0, 3.2);
    scene.add(key);
    const fill = new THREE.DirectionalLight(0x9fc4ff, 0.75);
    fill.position.set(-3.0, 1.2, 1.6);
    scene.add(fill);
    const rim = new THREE.DirectionalLight(0xc8ff6a, 0.9);
    rim.position.set(-1.4, 1.8, -3.2);
    scene.add(rim);

    const grid = new THREE.GridHelper(4, 20, 0x2e332a, 0x1c1f19);
    scene.add(grid);
    const axes = new THREE.AxesHelper(0.35);
    scene.add(axes);

    // Orbit state, hand-rolled: OrbitControls would be another dependency for
    // behaviour this small.
    const target = new THREE.Vector3(0, 1.2, 0);
    const spherical = new THREE.Spherical(2.4, Math.PI / 2.35, 0);

    const state = { useOrtho: false };

    const applyCamera = () => {
      const offset = new THREE.Vector3().setFromSpherical(spherical);
      camera.position.copy(target).add(offset);
      camera.lookAt(target);

      ortho.position.copy(camera.position);
      ortho.lookAt(target);

      // The orthographic frustum tracks the orbit radius so zooming feels the
      // same in both projections.
      const aspect = camera.aspect || 1;
      const half = spherical.radius * 0.35;
      ortho.left = -half * aspect;
      ortho.right = half * aspect;
      ortho.top = half;
      ortho.bottom = -half;
      ortho.updateProjectionMatrix();
    };
    applyCamera();

    let frames = 0;
    let lastSample = performance.now();
    let running = true;

    const frame = () => {
      if (!running) return;
      renderer.render(scene, state.useOrtho ? ortho : camera);

      frames++;
      const now = performance.now();
      if (now - lastSample >= 1000) {
        setStats({ fps: Math.round((frames * 1000) / (now - lastSample)) });
        frames = 0;
        lastSample = now;
      }
      requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);

    /* ------------------------------------------------------ interaction */

    let dragging: 'orbit' | 'pan' | null = null;
    let lastX = 0;
    let lastY = 0;

    const onDown = (e: PointerEvent) => {
      dragging = e.button === 1 || e.shiftKey ? 'pan' : 'orbit';
      lastX = e.clientX;
      lastY = e.clientY;
      renderer.domElement.setPointerCapture(e.pointerId);
    };

    const onMove = (e: PointerEvent) => {
      if (!dragging) return;
      const dx = e.clientX - lastX;
      const dy = e.clientY - lastY;
      lastX = e.clientX;
      lastY = e.clientY;

      if (dragging === 'orbit') {
        spherical.theta -= dx * 0.0075;
        spherical.phi = Math.max(0.05, Math.min(Math.PI - 0.05, spherical.phi - dy * 0.0075));
      } else {
        const panScale = spherical.radius * 0.0016;
        const right = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 0);
        const up = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 1);
        target.addScaledVector(right, -dx * panScale);
        target.addScaledVector(up, dy * panScale);
      }
      applyCamera();
    };

    const onUp = (e: PointerEvent) => {
      dragging = null;
      try { renderer.domElement.releasePointerCapture(e.pointerId); } catch { /* already released */ }
    };

    const onWheel = (e: WheelEvent) => {
      e.preventDefault();
      spherical.radius = Math.max(0.35, Math.min(14, spherical.radius * (1 + Math.sign(e.deltaY) * 0.11)));
      applyCamera();
    };

    renderer.domElement.addEventListener('pointerdown', onDown);
    renderer.domElement.addEventListener('pointermove', onMove);
    renderer.domElement.addEventListener('pointerup', onUp);
    renderer.domElement.addEventListener('wheel', onWheel, { passive: false });

    const resize = new ResizeObserver(() => {
      const w = host.clientWidth;
      const h = host.clientHeight;
      if (w === 0 || h === 0) return;
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
    });
    resize.observe(host);

    engine.current = {
      renderer, scene, camera, ortho, target, spherical, grid, axes,
      get useOrtho() { return state.useOrtho; },
      set useOrtho(v: boolean) { state.useOrtho = v; },
      active: () => (state.useOrtho ? ortho : camera),
      frame: applyCamera,
      dispose: () => {
        running = false;
        resize.disconnect();
        renderer.domElement.removeEventListener('pointerdown', onDown);
        renderer.domElement.removeEventListener('pointermove', onMove);
        renderer.domElement.removeEventListener('pointerup', onUp);
        renderer.domElement.removeEventListener('wheel', onWheel);

        // Every GPU resource this viewport created has to go back, or a few
        // project switches leak enough texture memory to matter.
        const engineRef = engine.current;
        engineRef?.mesh?.geometry.dispose();
        engineRef?.material?.map?.dispose();
        engineRef?.material?.dispose();
        engineRef?.normalMaterial?.dispose();
        engineRef?.uvMaterial?.map?.dispose();
        engineRef?.uvMaterial?.dispose();
        engineRef?.wire?.geometry.dispose();
        (engineRef?.wire?.material as THREE.Material | undefined)?.dispose();
        engineRef?.normalsHelper?.geometry.dispose();
        (engineRef?.normalsHelper?.material as THREE.Material | undefined)?.dispose();
        grid.geometry.dispose();
        (grid.material as THREE.Material).dispose();
        axes.geometry.dispose();
        (axes.material as THREE.Material).dispose();

        renderer.dispose();
        host.removeChild(renderer.domElement);
      },
    };

    return () => {
      engine.current?.dispose();
      engine.current = null;
    };
  }, []);

  /* ----------------------------------------------------------- geometry */

  useEffect(() => {
    const e = engine.current;
    if (!e || !meshInfo) return;

    // Tear the previous drawable down completely. Leaving a geometry or a
    // texture behind here is how a long editing session ends up out of memory.
    if (e.mesh) {
      e.scene.remove(e.mesh);
      e.mesh.geometry.dispose();
      e.mesh = undefined;
    }
    e.material?.map?.dispose();
    e.material?.dispose();
    e.material = undefined;
    e.normalMaterial?.dispose();
    e.normalMaterial = undefined;
    e.uvMaterial?.map?.dispose();
    e.uvMaterial?.dispose();
    e.uvMaterial = undefined;

    if (e.wire) {
      e.scene.remove(e.wire);
      e.wire.geometry.dispose();
      (e.wire.material as THREE.Material).dispose();
      e.wire = undefined;
    }
    if (e.box) {
      e.scene.remove(e.box);
      e.box.geometry.dispose();
      (e.box.material as THREE.Material).dispose();
      e.box = undefined;
    }
    if (e.normalsHelper) {
      e.scene.remove(e.normalsHelper);
      e.normalsHelper.geometry.dispose();
      (e.normalsHelper.material as THREE.Material).dispose();
      e.normalsHelper = undefined;
    }

    let positions: Float32Array | null = null;
    let normals: Float32Array | null = null;
    let uvs: Float32Array | null = null;
    let indices: Uint32Array | null = null;

    if (meshInfo.buffer && meshInfo.layout) {
      const decoded = decodeBuffer(meshInfo.buffer, meshInfo.layout);
      positions = decoded.positions;
      normals = decoded.normals;
      uvs = decoded.uvs;
      indices = decoded.indices;
    } else if (meshInfo.positions) {
      positions = new Float32Array(meshInfo.positions);
      normals = meshInfo.normals ? new Float32Array(meshInfo.normals) : null;
      uvs = meshInfo.uvs ? new Float32Array(meshInfo.uvs) : null;
      indices = meshInfo.indices ? new Uint32Array(meshInfo.indices) : null;
    }

    if (!positions || positions.length === 0) return;

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    if (uvs) geometry.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));
    if (indices) geometry.setIndex(new THREE.BufferAttribute(indices, 1));

    // Only trust supplied normals when they are actually populated; the host
    // sends zeroes rather than inventing them, and zero-length normals render
    // black. Deriving them is honest and correct.
    const normalsUsable = normals
      && normals.length === positions.length
      && normals.some((v) => v !== 0);
    if (normalsUsable) geometry.setAttribute('normal', new THREE.BufferAttribute(normals!, 3));

    // RAGE is Z-up; Three.js is Y-up. Bake the conversion into the vertex data
    // rather than rotating the object, so bounds and the UV overlay are all
    // computed in the space the camera works in. This transforms normals too,
    // which is why they are set first.
    geometry.rotateX(-Math.PI / 2);

    if (!normalsUsable) geometry.computeVertexNormals();

    const material = new THREE.MeshStandardMaterial({
      color: 0xd8dcd2,
      roughness: 0.72,
      metalness: 0.04,
      side: THREE.DoubleSide,
    });

    const mesh = new THREE.Mesh(geometry, material);
    e.scene.add(mesh);
    e.mesh = mesh;
    e.material = material;
    e.normalMaterial = new THREE.MeshNormalMaterial({ side: THREE.DoubleSide });
    e.uvMaterial = new THREE.MeshBasicMaterial({
      map: makeUvChecker(), side: THREE.DoubleSide,
    });

    const wireGeometry = new THREE.WireframeGeometry(geometry);
    const wire = new THREE.LineSegments(
      wireGeometry,
      new THREE.LineBasicMaterial({ color: 0xa8e10c, transparent: true, opacity: 0.32 }));
    wire.visible = false;
    e.scene.add(wire);
    e.wire = wire;

    const box = new THREE.BoxHelper(mesh, 0xe08c3c);
    box.visible = false;
    e.scene.add(box);
    e.box = box;

    // Frame the garment: centre the orbit target on it and back off to fit.
    geometry.computeBoundingSphere();
    const sphere = geometry.boundingSphere;
    if (sphere) {
      e.target.copy(sphere.center);
      e.spherical.radius = Math.max(0.5, sphere.radius * 3.1);
      e.spherical.theta = 0;
      e.spherical.phi = Math.PI / 2.25;
      e.frame();
      e.grid.position.y = sphere.center.y - sphere.radius;
    }
  }, [meshInfo]);

  /* --------------------------------------------------- overlays and camera */

  useEffect(() => {
    const e = engine.current;
    if (!e) return;
    if (e.box) e.box.visible = showBounds;
  }, [showBounds, meshInfo]);

  // Vertex normals, drawn from the real attribute. A developer-mode check for
  // whether an imported drawable actually carries usable normals.
  useEffect(() => {
    const e = engine.current;
    if (!e?.mesh) return;

    if (e.normalsHelper) {
      e.scene.remove(e.normalsHelper);
      e.normalsHelper.geometry.dispose();
      (e.normalsHelper.material as THREE.Material).dispose();
      e.normalsHelper = undefined;
    }
    if (!showNormals) return;

    const positions = e.mesh.geometry.getAttribute('position');
    const normals = e.mesh.geometry.getAttribute('normal');
    if (!positions || !normals) return;

    const sphere = e.mesh.geometry.boundingSphere;
    const length = (sphere?.radius ?? 1) * 0.035;
    // One in eight vertices: enough to read direction, few enough to stay fast
    // on a 15,000-vertex garment.
    const stride = Math.max(1, Math.floor(positions.count / 3000));
    const points: number[] = [];

    for (let i = 0; i < positions.count; i += stride) {
      const x = positions.getX(i), y = positions.getY(i), z = positions.getZ(i);
      points.push(x, y, z,
        x + normals.getX(i) * length,
        y + normals.getY(i) * length,
        z + normals.getZ(i) * length);
    }

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(points, 3));
    const helper = new THREE.LineSegments(
      geometry, new THREE.LineBasicMaterial({ color: 0x59a7e0, transparent: true, opacity: 0.6 }));
    e.scene.add(helper);
    e.normalsHelper = helper;
  }, [showNormals, meshInfo]);

  useEffect(() => {
    const e = engine.current;
    if (!e) return;
    e.useOrtho = orthographic;
    e.frame();
  }, [orthographic]);

  useEffect(() => {
    const e = engine.current;
    if (!e) return;
    e.camera.fov = fov;
    e.camera.updateProjectionMatrix();
  }, [fov]);

  /* ------------------------------------------------------------ shading */

  useEffect(() => {
    const e = engine.current;
    if (!e?.mesh || !e.material) return;

    if (e.wire) e.wire.visible = shading === 'wireframe';
    e.mesh.visible = shading !== 'wireframe';

    switch (shading) {
      case 'normals':
        // Real surface normals as colour: the standard way to spot flipped or
        // missing normals on an imported drawable.
        if (e.normalMaterial) e.mesh.material = e.normalMaterial;
        break;

      case 'uv':
        if (e.uvMaterial) e.mesh.material = e.uvMaterial;
        break;

      case 'solid':
        e.mesh.material = e.material;
        e.material.map = null;
        e.material.color.set(0xd8dcd2);
        e.material.needsUpdate = true;
        break;

      default:
        e.mesh.material = e.material;
        e.material.color.set(0xffffff);
        e.material.needsUpdate = true;
        break;
    }
  }, [shading, meshInfo]);

  /* ------------------------------------------------------------ texture */

  useEffect(() => {
    const e = engine.current;
    if (!e?.material) return;

    if (!textureUrl || shading !== 'material') {
      if (e.material.map) {
        e.material.map.dispose();
        e.material.map = null;
        e.material.color.set(0xd8dcd2);
        e.material.needsUpdate = true;
      }
      return;
    }

    const loader = new THREE.TextureLoader();
    loader.load(textureUrl, (texture) => {
      texture.colorSpace = THREE.SRGBColorSpace;
      texture.flipY = false; // GTA UVs are top-left origin
      texture.anisotropy = 8;
      if (e.material) {
        e.material.map?.dispose();
        e.material.map = texture;
        e.material.color.set(0xffffff);
        e.material.needsUpdate = true;
      }
    });
  }, [textureUrl, shading]);

  useEffect(() => {
    const e = engine.current;
    if (!e) return;
    e.grid.visible = showGrid;
    e.axes.visible = showGrid;
  }, [showGrid]);

  /* ------------------------------------------------------------ presets */

  const setCamera = (preset: CameraPreset) => {
    const e = engine.current;
    if (!e) return;

    const angles: Record<Exclude<CameraPreset, 'reset'>, [number, number]> = {
      front: [0, Math.PI / 2],
      back: [Math.PI, Math.PI / 2],
      left: [-Math.PI / 2, Math.PI / 2],
      right: [Math.PI / 2, Math.PI / 2],
      top: [0, 0.08],
      bottom: [0, Math.PI - 0.08],
    };

    if (preset === 'reset') {
      e.spherical.theta = 0;
      e.spherical.phi = Math.PI / 2.25;
      if (e.mesh?.geometry.boundingSphere) {
        e.target.copy(e.mesh.geometry.boundingSphere.center);
        e.spherical.radius = Math.max(0.5, e.mesh.geometry.boundingSphere.radius * 3.1);
      }
    } else {
      const [theta, phi] = angles[preset];
      e.spherical.theta = theta;
      e.spherical.phi = phi;
    }
    e.frame();
  };

  /* --------------------------------------------------------------- render */

  return (
    <div className="viewport">
      <div className="viewport-toolbar">
        <div className="vt-group">
          {(['front', 'back', 'left', 'right', 'top', 'bottom'] as const).map((p) => (
            <button key={p} className="vt-btn" onClick={() => setCamera(p)} title={`${p} view`}>
              {p[0].toUpperCase()}
            </button>
          ))}
          <button className="vt-btn" onClick={() => setCamera('reset')} title="Frame selected (F)">⤢</button>
        </div>

        <div className="vt-sep" />

        <div className="vt-group">
          {([
            ['material', 'Textured, using the composited diffuse'],
            ['solid', 'Flat shading, no texture'],
            ['wireframe', 'Edges only'],
            ['uv', 'A UV checker mapped through the drawable’s own coordinates'],
            ['normals', 'Surface normals as colour'],
          ] as const).map(([m, hint]) => (
            <button
              key={m}
              className={`vt-btn wide${shading === m ? ' active' : ''}`}
              onClick={() => setShading(m)}
              title={hint}
            >
              {m}
            </button>
          ))}
        </div>

        <div className="vt-sep" />

        <button
          className={`vt-btn wide${showGrid ? ' active' : ''}`}
          onClick={() => setShowGrid((v) => !v)}
          title="Grid and axes"
        >grid</button>
        <button
          className={`vt-btn wide${showBounds ? ' active' : ''}`}
          onClick={() => setShowBounds((v) => !v)}
          title="Bounding box of the loaded geometry"
        >bounds</button>
        {developerMode && (
          <button
            className={`vt-btn wide${showNormals ? ' active' : ''}`}
            onClick={() => setShowNormals((v) => !v)}
            title="Draw vertex normals (developer mode)"
          >norm</button>
        )}
        <button
          className={`vt-btn wide${orthographic ? ' active' : ''}`}
          onClick={() => setOrthographic((v) => !v)}
          title={orthographic ? 'Switch to perspective' : 'Switch to orthographic'}
        >{orthographic ? 'ortho' : 'persp'}</button>

        {!orthographic && (
          <label className="vt-fov" title="Field of view">
            <input
              type="range" min={16} max={80} value={fov}
              onChange={(e) => setFov(Number(e.target.value))}
            />
            <span className="mono dim">{fov}°</span>
          </label>
        )}

        <span className="grow" />

        {meshInfo?.isMock && (
          <Badge kind="mock" title="Synthetic geometry, not a real GTA asset">Mock asset</Badge>
        )}
        {meshInfo && !meshInfo.isMock && <Badge kind="ok">Real asset</Badge>}

        {onToggleFullscreen && (
          <button
            className="vt-btn"
            onClick={onToggleFullscreen}
            title={fullscreen ? 'Leave fullscreen viewport (F11)' : 'Fullscreen viewport (F11)'}
          >{fullscreen ? '⤡' : '⤢'}</button>
        )}
      </div>

      <div className="viewport-canvas" ref={mount}>
        {meshLoading && (
          <div className="viewport-overlay">
            <Spinner size={18} />
            <span className="dim">Loading model…</span>
          </div>
        )}

        {!meshLoading && !meshInfo && !meshError && (
          <div className="viewport-overlay">
            <EmptyState
              title="No model loaded"
              detail="Open a project or pick a drawable from the asset browser."
              action={<Button size="sm" onClick={() => loadMesh(null)}>Load mock torso</Button>}
            />
          </div>
        )}

        {meshError && (
          <div className="viewport-overlay">
            <EmptyState tone="error" title="The model could not be loaded" detail={meshError} />
          </div>
        )}
      </div>

      <div className="viewport-status">
        <span className="mono">{meshInfo ? meshInfo.source : '—'}</span>
        <span className="grow" />
        <span className="mono dim">
          {meshInfo ? `${meshInfo.vertexCount.toLocaleString()} v` : '—'}
        </span>
        <span className="mono dim">
          {meshInfo ? `${meshInfo.triangleCount.toLocaleString()} tri` : '—'}
        </span>
        <span className="mono dim">LOD {meshInfo?.lod ?? '—'}</span>
        <span className="mono dim">{stats ? `${stats.fps} fps` : '—'}</span>
      </div>
    </div>
  );
}
