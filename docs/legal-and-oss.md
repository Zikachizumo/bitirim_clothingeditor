# Open Source & Legal Assessment

Licences were verified via the GitHub licence API, by reading `LICENSE`/`NOTICE` files directly, or
by reading source-file headers. Where a project has no licence, that is stated as a finding, not
glossed over.

## 1. Verdict table

| Project | Licence | Verified how | Verdict |
|---|---|---|---|
| [Hancapo/fivefury](https://github.com/Hancapo/fivefury) | **The Unlicense** (public domain) | GitHub licence API + PyPI metadata | ✅ **Use.** Primary asset-engine dependency |
| [Nominom/BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) | **The Unlicense** | GitHub licence API | ✅ **Use.** BC1–BC7 / DDS encoding, no native deps |
| [Neodymium146/gta-toolkit (RageLib)](https://github.com/Neodymium146/gta-toolkit) | **MIT** via per-file headers (`Copyright(c) 2016 Neodymium`) — no root `LICENSE` | read `ResourceFileTypes_GTA5_pc.cs` header + `README.txt` | ⚠️ **Usable with attribution.** But `README.txt` reads `THIS PROJECT IS DISCONTINUED.` Fallback only |
| [dexyfex/CodeWalker](https://github.com/dexyfex/CodeWalker) | **None — all rights reserved** | licence API returns 404; `Readme_Src.txt`: *"This source code is released for educational purposes only."* | ❌ **Do not copy.** Reference the approach only |
| CodeWalker.Core on NuGet | published by a third party ("Ancapo"), not by dexyfex | NuGet package page | ❌ **Do not depend on.** Republishing does not create a grant |
| [Sollumz/Sollumz](https://github.com/Sollumz/Sollumz) | **GPL-3.0** | GitHub licence API | ❌ **Do not link or copy** into a proprietary product. Its *documentation* is fine to learn from and cite |
| [kngrektor/ytdtool](https://github.com/kngrektor/ytdtool) | none found | repo inspection | ❌ Code unusable; fine as a correctness cross-check |
| Texture Toolkit (Neodymium) | closed, no source | GTA5-Mods page | ❌ Not a code source |
| 0RESMON Clothing Designer (the reference) | commercial, FiveM escrow | product listing | ❌ **Never** decompile, extract, or reverse-engineer |
| [Durty Cloth Tool](https://gta.clothing/) | commercial, subscription | product site | ❌ Competitor. Evaluate as a user; copy nothing |
| Three.js | MIT | — | ✅ Use |
| [googleapis/dotnet-genai (`Google.GenAI`)](https://github.com/googleapis/dotnet-genai) | **Apache-2.0** | package metadata, published by Google LLC | ✅ **Use.** AI texture generation. Apache-2.0 needs the NOTICE and licence preserved on redistribution |
| SQLite / Microsoft.Data.Sqlite | Public domain / MIT | — | ✅ Use |

## 2. Rules for this project

1. **Copy code only from Unlicense or MIT sources**, preserving copyright notices and licence text
   in `THIRD-PARTY-NOTICES.md`.
2. **Never copy from CodeWalker.** Its absence of a licence means all rights reserved. Reading it to
   understand a format is normal engineering practice; pasting its code into our repo is
   infringement. If a developer has read CodeWalker source recently, they should implement from the
   *format description*, not from memory of the code.
3. **Never link or copy Sollumz.** GPL-3.0 would force the entire product to GPL. If the project
   later decides to *be* GPL, this can be revisited — see `prd.md` §9 q2.
4. **Never touch the reference product's escrowed code.** Our approach throughout has been
   observational: we watched the UI, inventoried behaviour, and designed our own solutions to the
   same problems. That is legitimate. Decompiling FiveM escrow is not, and would also breach its
   terms of service.
5. **Ship no Rockstar assets.** No extracted meshes, textures, skeletons, or metadata in the repo or
   the installer. Test fixtures must be either user-supplied at test time or freely-licensed
   community assets whose terms permit redistribution — and their provenance recorded in
   `tests/fixtures/PROVENANCE.md`.
6. **Dependency audit is a release gate.** Every direct and transitive dependency's licence is
   checked before any build ships.

## 3. On the reference product specifically

The brief asks for *feature equivalence*, not imitation. Concretely, for this project that means:

- **Allowed:** observing the UI in a public marketing video; cataloguing what it does; noting that
  addon clothing packs use a particular naming convention (that convention is community knowledge,
  documented publicly on the cfx.re forums, not the vendor's invention); designing our own UI that
  solves the same workflow.
- **Not allowed:** reproducing its visual design, its brand (`0R`, `Oresmon`, `Clothing Designer`),
  its icons or copy; extracting or decompiling its resource; presenting our product as compatible
  with or derived from it.

Our UI should look like a professional desktop tool, not like the reference's NUI. The architecture
in `architecture.md` diverges substantially from it anyway — different deployment model, different
process structure, different project persistence.

## 4. GTA V / FiveM ecosystem considerations

- Tools that read game files are ubiquitous and tolerated, but Rockstar's EULA prohibits
  distributing game assets. Our position — read the user's own files, redistribute nothing — is the
  standard one.
- FiveM's terms govern what runs on a server. Our optional `FiveMBridge` (`architecture.md` §2)
  would be an ordinary resource and should be written to be obviously benign: no obfuscation, no
  escrow, no network calls beyond localhost.
- If the product is ever sold, add a clear statement that it ships no game assets and requires the
  user to own GTA V.

## 5. Attribution file

`THIRD-PARTY-NOTICES.md` must be generated before first release and shipped with the installer,
listing every dependency, its version, its licence text, and its source URL. For `fivefury` and
`BCnEncoder.NET` the Unlicense imposes no obligation, but attributing them anyway is the right call —
they are the reason this product is buildable.
