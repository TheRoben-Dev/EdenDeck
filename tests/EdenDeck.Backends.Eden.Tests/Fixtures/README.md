# Eden config fixtures

These are **hand-written** configs in the exact format Eden's `frontend_common/config.cpp`
produces (key encoding, `\default`, `\use_global`, enum indices, QSettings arrays), built
from reading the source at the versions named in the folder names. They are not copies of
files written by a real Eden install. Replace or add real files from a live install as
they become available (one folder per Eden version); the tests run against every folder.
