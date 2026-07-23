#!/bin/bash
# Shared FFmpeg source-prep + configure/build helpers for the BMS supplemental
# native backend. Mirrors osu-framework's common.sh layout, but builds STATIC
# archives (the framework builds shared) so build-win.sh can link them into a
# single bms-ffmpeg.dll with --whole-archive.
set -eu

# Pinned to the 4.3 branch to match the FFmpeg.AutoGen 4.3.0.1 managed bindings the
# decoder dereferences through (AVFormatContext/AVStream/AVCodecParameters field
# offsets are baked into those bindings). A native build from a different major
# version shifts struct layouts and corrupts the heap on the first avformat/avcodec
# call. 4.3.9 is the last 4.3 maintenance release; ABI is stable across 4.3.x.
FFMPEG_VERSION="4.3.9"
FFMPEG_FILE="ffmpeg-$FFMPEG_VERSION.tar.gz"

# Decoder/demuxer set derived from scanning D:\BMS\LargePack. Video support only
# includes legacy codecs osu-framework's bundled FFmpeg lacks; FLAC is included
# for BMS packs that replace chart-declared WAV files with lossless equivalents.
FFMPEG_FLAGS=(
    --disable-shared
    --enable-static
    --disable-debug
    --disable-all
    --disable-autodetect
    --enable-lto

    --enable-avcodec
    --enable-avformat
    --enable-swscale

    --enable-demuxer='mpegps,mpegvideo,asf,avi,flac'
    --enable-parser='mpegvideo,vc1,flac'
    --enable-decoder='mpeg1video,mpeg2video,vc1,wmv1,wmv3,mss2,msvideo1,cinepak,flac'

    --enable-protocol=pipe
)

function prep_ffmpeg() {
    FFMPEG_FLAGS+=(
        --prefix="$PWD/$1"
    )

    local build_dir="$1-build"
    if [ ! -e "$FFMPEG_FILE" ]; then
        echo "-> Downloading $FFMPEG_FILE..."
        curl -o "$FFMPEG_FILE" "https://ffmpeg.org/releases/$FFMPEG_FILE"
    else
        echo "-> $FFMPEG_FILE already exists, not re-downloading."
    fi

    if [ ! -d "$build_dir" ]; then
        echo "-> Unpacking source to $build_dir..."
        mkdir "$build_dir"
        tar xzf "$FFMPEG_FILE" --strip 1 -C "$build_dir"
    else
        echo "-> $build_dir already exists, skipping unpacking."
    fi

    cd "$build_dir"
}

function build_ffmpeg() {
    echo "-> Configuring..."
    ./configure "${FFMPEG_FLAGS[@]}"

    echo "-> Building using $CORES threads..."
    make -j$CORES
}

CORES=0
if [[ "$OSTYPE" == "darwin"* ]]; then
    CORES=$(sysctl -n hw.ncpu)
else
    CORES=$(nproc)
fi
