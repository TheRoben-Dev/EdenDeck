// SPDX-License-Identifier: MIT
#include <cstdio>
#include <cstdlib>

#include "crt_settings.hpp"

using scanline::CrtLook;
using scanline::CrtState;
using scanline::ParseCrtControl;

static int failures = 0;
#define CHECK(c)                                                       \
    do {                                                               \
        if (!(c)) {                                                    \
            std::fprintf(stderr, "%s:%d: CHECK(%s)\n", __FILE__, __LINE__, #c); \
            ++failures;                                                \
        }                                                              \
    } while (0)

int main() {
    CrtState s;
    s = ParseCrtControl("level=2\nmotion=0\n", s);
    CHECK(s.level == 2 && !s.motion);
    s = ParseCrtControl("level=0\r\nmotion=1", s);
    CHECK(s.level == 0 && s.motion);
    // Bad values keep the previous state.
    s = ParseCrtControl("level=7\nlevel=\nmotion=yes\nfoo=1\n", CrtState{3, false});
    CHECK(s.level == 3 && !s.motion);
    s = ParseCrtControl("", CrtState{1, true});
    CHECK(s.level == 1 && s.motion);
    // Same numbers as the app's Scanlines.cs levels 1-3; off draws nothing.
    CHECK(CrtLook::ForLevel(0).strength == 0.0f);
    CHECK(CrtLook::ForLevel(1).strength == 0.7f && CrtLook::ForLevel(1).curve == 0.02f);
    CHECK(CrtLook::ForLevel(2).strength == 1.0f && CrtLook::ForLevel(2).vignette == 0.25f);
    CHECK(CrtLook::ForLevel(3).strength == 1.4f && CrtLook::ForLevel(3).chroma == 1.5f);
    if (failures == 0) std::puts("crt_settings_test: ok");
    return failures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
