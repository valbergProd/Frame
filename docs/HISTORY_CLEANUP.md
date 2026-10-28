# Repository history cleanup — 2026-10-28

At the owner's explicit request, all published history was rewritten with git-filter-repo 2.47.0 to remove personal agent/handover files. Only AGENTS.md and CLAUDE.md had ever been tracked. Handover_files and the other ignored personal handoff paths had not been published. Local files remain available and ignored; a local backup bundle and private-file copies were retained.

Both published refs were updated with atomic, explicitly leased force-pushes:

- main: e5e98b8300ad3758976787fbda3dc4dc1cfd4e98 → 8d14988e9cf008ad99c3bb6ffec49cfc61517615 (before this documentation follow-up).
- v1.0.0: 48a6bbfd09178e7231a5d356573b634b560404e4 → d4efa4b7aef99187a2f92bacfb0d43c12376cdc2.

All 11 commits were rewritten. Current main and release-tag file trees were byte-identical before/after the rewrite. A fresh mirror fetched from GitHub contains no removed paths in any reachable commit. There were no pull requests or other published refs. Local main was synchronized without replacing working files.

Release v1.0.0 remains public/latest, with the same release ID 427193932 and installer asset ID 686350181. Installer size remains 66,114,885 bytes; SHA-256 remains E67E0BF31CA8FD8EB69990EA465434E45491CFC598EEDE9452C7340AB3064237. No new app build was needed: executable source and distributed bytes are unchanged. Original commit IDs embedded in the binary and historical validation reports describe the pre-cleanup history.

## GitHub cache limitation

After the force-push, an authenticated direct file request at the original first commit still returned successfully. Rewriting refs does not erase GitHub's unreachable/cached commit objects. [GitHub documents this limitation](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository) and handles eligible cache/reference purges through Support; Support may decline non-sensitive material. No Support request was sent and no full cache erasure is claimed.

If GitHub Support is contacted, repository: valbergProd/Frame; first changed commit: 610b888dea346767fb75389359b83ce5b67d2f7e; affected pull requests: zero; LFS: not in use. Other pre-cleanup clones should be recloned or carefully rebased; merging old history could restore the removed files. The private local backup must never be pushed.
