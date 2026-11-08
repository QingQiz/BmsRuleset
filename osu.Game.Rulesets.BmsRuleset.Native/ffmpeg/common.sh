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
DAV1D_VERSION="0.9.2"
DAV1D_FILE="dav1d-$DAV1D_VERSION.tar.xz"

# Keep container and decoder support together: LargePack includes MPEG video in
# MP4 containers and H.264 in MPEG containers.
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
    --enable-libdav1d

    --enable-demuxer='mpegps,mpegvideo,h264,mov,asf,avi,flac'
    --enable-parser='mpegvideo,h264,av1,vc1,mjpeg,flac'
    --enable-decoder='mpeg1video,mpeg2video,h264,libdav1d,vc1,wmv1,wmv3,mss2,msvideo1,cinepak,mjpeg,rawvideo,dvvideo,flac'

    --enable-protocol=pipe
)

function build_dav1d() {
    local target="$1"
    local prefix="$PWD/$target-deps"
    local build_dir="$PWD/dav1d-$target-build"
    local cross_file="$PWD/dav1d-$target.ini"

    if [ ! -e "$DAV1D_FILE" ]; then
        curl --fail --location -o "$DAV1D_FILE" "https://downloads.videolan.org/pub/videolan/dav1d/$DAV1D_VERSION/$DAV1D_FILE"
    fi
    echo 'e3235ab6c43c0135b0db1d131e1923fad4c84db9d85683e30b91b33a52d61c71  '"$DAV1D_FILE" | sha256sum -c -

    if [ ! -d "dav1d-$DAV1D_VERSION" ]; then
        tar xf "$DAV1D_FILE"
    fi

    local cross_args=()
    if [ "$target" != "linux-x64" ]; then
        local system=windows
        local compiler="${cross_prefix}gcc"
        if [ "$target" == "osx" ]; then
            system=darwin
            compiler="$clang_wrapper"
        fi
        cat > "$cross_file" <<EOF
[binaries]
c = '$compiler'
ar = '${cross_prefix}ar'
strip = '${cross_prefix}strip'
windres = '${cross_prefix}windres'
[host_machine]
system = '$system'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
EOF
        cross_args+=(--cross-file "$cross_file")
    fi

    meson setup "$build_dir" "dav1d-$DAV1D_VERSION" --prefix "$prefix" --libdir lib \
        --buildtype release --default-library static -Db_staticpic=true \
        -Denable_tools=false -Denable_tests=false "${cross_args[@]}"
    ninja -C "$build_dir" -j"$CORES" install
    export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
    FFMPEG_FLAGS+=(--pkg-config=pkg-config --extra-ldflags="-L$prefix/lib")
}

function prep_ffmpeg() {
    build_dav1d "$1"
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

if [ -n "${BMS_NATIVE_JOBS:-}" ]; then
    CORES="$BMS_NATIVE_JOBS"
elif [[ "$OSTYPE" == "darwin"* ]]; then
    CORES=$(sysctl -n hw.ncpu)
else
    CORES=$(nproc)
fi
