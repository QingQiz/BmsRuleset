#!/bin/bash
# Cross-compile FFmpeg for macOS x86_64 via osxcross and link the static
# archives into a single bms-ffmpeg.dylib (avutil/avcodec/avformat/swscale
# merged). The dylib is embedded into the ruleset assembly at build time and
# extracted to a temp path at load time, identical to the win-x64/linux-x64
# backends. Built on a Linux host with osxcross (cctools-port + Apple SDK) —
# no native macOS toolchain required.
set -eu

pushd "$(dirname "$0")" > /dev/null
SCRIPT_PATH=$(pwd)
popd > /dev/null
source "$SCRIPT_PATH/common.sh"

# osx/x86_64 only — the loader's IsAvailable gate declines every other target.
# osxcross installs wrappers as <target>/bin/x86_64-apple-darwinNN-clang; the
# darwin revision is baked by the SDK fed to osxcross, so discover it by glob
# rather than hardcoding. Override the search dir with OSXCROSS_BIN if needed.
osxcross_bin="${OSXCROSS_BIN:-$HOME/osxcross/target/bin}"
clang_wrapper=$(ls "$osxcross_bin"/x86_64-apple-darwin*-clang 2>/dev/null | head -1)
if [ -z "$clang_wrapper" ]; then
    echo "osxcross x86_64-apple-darwin*-clang not found under $osxcross_bin" >&2
    echo "build osxcross first (https://github.com/tpoechtrager/osxcross) or set OSXCROSS_BIN" >&2
    exit 1
fi
cross_prefix="${clang_wrapper%-clang}-"

# The wrapper execs clang with -target darwin, but clang then needs to find
# cctools-port's ld/ar/ranlib/strip (named with the cross prefix) on PATH.
# Without this, clang falls back to the host's GNU ld and fails with
# "unrecognised emulation mode: llvm". So prepend the wrapper's bin dir.
export PATH="$(dirname "$clang_wrapper"):$PATH"

out_dir="$SCRIPT_PATH/osx"
build_dir="$SCRIPT_PATH/osx-build"

FFMPEG_FLAGS+=(
    --enable-pic
    --enable-pthreads
    --enable-cross-compile
    --host-cc=clang
    --target-os=darwin
    --arch=x86_64
    --cross-prefix="$cross_prefix"
    # FFmpeg 4.3's darwin case doesn't set cc_default=clang, so --cross-prefix
    # alone leaves it looking for ${cross_prefix}gcc (absent). Point at the
    # clang wrapper explicitly.
    --cc="$clang_wrapper"
)

pushd . > /dev/null
prep_ffmpeg "osx"
build_ffmpeg
popd > /dev/null

# --- single-.dylib link (mirrors build-linux.sh / build-win.sh) ---
# -force_load per archive is ld64's equivalent of GNU ld's --whole-archive per
# archive: FFmpeg registers demuxers/codecs/parsers via static linker-section
# objects the linker otherwise drops, leaving avcodec_find_decoder returning NULL.
# All four archives are force-loaded (not just avformat+avcodec) because the
# managed loader resolves symbols at RUNTIME via NativeLibrary.GetExport, so
# avutil/swscale exports nothing in the C link references (av_frame_alloc,
# sws_scale, ...) must be force-exported too — scoping to drop dead audio code
# breaks the loader. No -Bsymbolic: that's a GNU-ld ELF option with no ld64
# meaning. EXTRALIBS is parsed from the config.mak FFmpeg wrote so the link
# line tracks its detected deps (resolved against the SDK's .tbd stubs).
config_mak="$build_dir/ffbuild/config.mak"
# NOTE: unlike build-linux.sh/build-win.sh, we do NOT split tokens onto lines
# and sort -u here. Darwin's EXTRALIBS contains "-framework <Name>" pairs that
# MUST stay adjacent and in order; sort -u would dedupe the three -framework
# flags into one and orphan CoreFoundation/CoreMedia/CoreVideo as bare file
# arguments (clang: "no such file or directory: 'CoreFoundation'"). Concatenate
# the raw values verbatim instead — duplicate flags across EXTRALIBS-* lines
# are harmless to ld64.
extralibs=$(grep -E '^EXTRALIBS' "$config_mak" | cut -d= -f2- | tr '\n' ' ')

echo "-> Linking single bms-ffmpeg.dylib (extralibs: $extralibs)..."
mkdir -p "$out_dir"
"${cross_prefix}clang" -dynamiclib \
    -Wl,-force_load,"$build_dir/libavformat/libavformat.a" \
    -Wl,-force_load,"$build_dir/libavcodec/libavcodec.a" \
    -Wl,-force_load,"$build_dir/libswscale/libswscale.a" \
    -Wl,-force_load,"$build_dir/libavutil/libavutil.a" \
    -o "$out_dir/bms-ffmpeg.dylib" \
    $extralibs

echo "-> Built $out_dir/bms-ffmpeg.dylib"
echo "-> Exported symbol count:"
"${cross_prefix}nm" "$out_dir/bms-ffmpeg.dylib" 2>/dev/null | grep -c ' T ' || true
