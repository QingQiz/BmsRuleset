#!/bin/bash
# Native Linux x64 build: link the static FFmpeg archives into a single bms-ffmpeg.so
# embedded into the ruleset and loaded the same way as the win-x64 DLL. Built natively
# (no cross-compile) since the host/WSL is linux-x64. PIC is mandatory so the static
# archives link cleanly into a shared object (Windows PE needs no PIC; ELF does).
set -eu

pushd "$(dirname "$0")" > /dev/null
SCRIPT_PATH=$(pwd)
popd > /dev/null
source "$SCRIPT_PATH/common.sh"

out_dir="$SCRIPT_PATH/linux-x64"
build_dir="$SCRIPT_PATH/linux-x64-build"

FFMPEG_FLAGS+=(
    --enable-pic
    --enable-pthreads
)

pushd . > /dev/null
prep_ffmpeg "linux-x64"
build_ffmpeg
popd > /dev/null

# --- single-.so link (mirrors build-win.sh) ---
# --whole-archive is mandatory: FFmpeg registers codecs/demuxers/parsers via static
# linker-section objects the linker otherwise drops as "unused", leaving
# avcodec_find_decoder returning NULL at runtime. EXTRALIBS is parsed from the
# config.mak FFmpeg wrote so the link line tracks its detected deps (e.g. -lm -lpthread).
config_mak="$build_dir/ffbuild/config.mak"
extralibs=$(grep -E '^EXTRALIBS' "$config_mak" | cut -d= -f2- | tr ' ' '\n' | grep -v '^$' | sort -u | tr '\n' ' ')

echo "-> Linking single bms-ffmpeg.so (extralibs: $extralibs)..."
mkdir -p "$out_dir"
# -Bsymbolic binds global references to the local definition within the .so. FFmpeg's x86 asm
# (built with -DPIC) still emits R_X86_64_PC32 relocations against internal symbols that, with
# default visibility, the linker refuses in a shared object ("recompile with -fPIC"). Binding
# them locally makes those PC32 offsets resolvable at link time — the .so is self-contained, so
# no interposition is lost.
gcc -shared -static-libgcc -Wl,-Bsymbolic -o "$out_dir/bms-ffmpeg.so" \
    -Wl,--whole-archive \
    "$build_dir/libavformat/libavformat.a" \
    "$build_dir/libavcodec/libavcodec.a" \
    "$build_dir/libswscale/libswscale.a" \
    "$build_dir/libavutil/libavutil.a" \
    -Wl,--no-whole-archive \
    $extralibs

echo "-> Built $out_dir/bms-ffmpeg.so"
echo "-> Exported symbol count:"
nm "$out_dir/bms-ffmpeg.so" 2>/dev/null | grep -c ' T ' || true
