"""Versioned contract between the .NET host and the Python asset service.

The host owns product semantics (projects, layers, undo, export orchestration).
This service only ever speaks in terms of *files and buffers*: it opens a RAGE
resource and describes it, or it takes bytes and writes a RAGE resource.

Keeping the contract this narrow is what makes the fivefury dependency
replaceable -- see docs/technology-comparison.md section 3.

Protocol: newline-delimited JSON over stdio.
  request  {"id": <int>, "op": <str>, "args": {...}}
  response {"id": <int>, "ok": true,  "result": {...}}
           {"id": <int>, "ok": false, "error": {"code": <str>, "message": <str>}}

Bulk binary payloads are never inlined. They are passed by file path; the host
writes them to a temp file and the service reads them back (and vice versa).
"""

CONTRACT_VERSION = "1.0.0"

# Operations implemented by this build. The host reads this at startup and
# gates UI affordances on it -- a capability we cannot deliver must not be
# offered. See docs/architecture.md section 7.
OPERATIONS = (
    "capabilities",
    "inspect",
    "ytd_read",
    "ytd_decode",
    "ytd_replace_texture",
    "ydd_read",
    "ydd_mesh",
    "ymt_read",
    "ymt_build_addon",
    "validate_pack",
)


class OpError(Exception):
    """An error with a stable code the host can branch on."""

    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code
        self.message = message


# Stable error codes. The host maps these to user-facing messages; raw
# exception text never reaches the user (docs/architecture.md section 7).
E_UNKNOWN_OP = "unknown_op"
E_BAD_ARGS = "bad_args"
E_NOT_FOUND = "not_found"
E_UNSUPPORTED_FORMAT = "unsupported_format"
E_PARSE_FAILED = "parse_failed"
E_WRITE_FAILED = "write_failed"
E_INTERNAL = "internal"
