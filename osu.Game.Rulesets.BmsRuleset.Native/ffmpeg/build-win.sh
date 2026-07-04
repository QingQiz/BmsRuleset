#!/bin/bash
# Cross-compile FFmpeg for Windows x64 and link the static archives into a
# single bms-ffmpeg.dll (avutil/avcodec/avformat/swscale merged). The DLL is
# embedded into the ruleset assembly at build time and extracted to a temp path
# at load time.
set -eu

pushd "$(dirname "$0")" > /dev/null
SCRIPT_PATH=$(pwd)
popd > /dev/null
source "$SCRIPT_PATH/common.sh"

# win-x64 only — the loader's IsAvailable gate declines every other target.
cross_prefix='x86_64-w64-mingw32-'
out_dir="$SCRIPT_PATH/windows-x64"
build_dir="$SCRIPT_PATH/win-x64-build"

FFMPEG_FLAGS+=(
    --enable-w32threads
    --enable-cross-compile
    --target-os=mingw32
    --arch=x86_64
    --cross-prefix=$cross_prefix
)

pushd . > /dev/null
prep_ffmpeg "win-x64"
build_ffmpeg
popd > /dev/null

# --- single-DLL link -------------------------------------------------------
# --whole-archive is mandatory: FFmpeg registers codecs/demuxers/parsers via
# static linker-section objects the linker otherwise drops as "unused", which
# would leave avcodec_find_decoder returning NULL at runtime. Pull the exact
# transitive link flags (EXTRALIBS-*) from the config.mak FFmpeg itself wrote
# rather than guessing -lm/-lws2_32/-lbcrypt etc.
config_mak="$build_dir/ffbuild/config.mak"
extralibs=$(grep -E '^EXTRALIBS' "$config_mak" | cut -d= -f2- | tr ' ' '\n' | grep -v '^$' | sort -u | tr '\n' ' ')

echo "-> Linking single bms-ffmpeg.dll (extralibs: $extralibs)..."
mkdir -p "$out_dir"
${cross_prefix}gcc -shared -static-libgcc -o "$out_dir/bms-ffmpeg.dll" \
    -Wl,--whole-archive \
    "$build_dir/libavformat/libavformat.a" \
    "$build_dir/libavcodec/libavcodec.a" \
    "$build_dir/libswscale/libswscale.a" \
    "$build_dir/libavutil/libavutil.a" \
    -Wl,--no-whole-archive \
    $extralibs

echo "-> Built $out_dir/bms-ffmpeg.dll"
echo "-> Exported symbol count:"
${cross_prefix}nm "$out_dir/bms-ffmpeg.dll" | grep -c ' T ' || true
