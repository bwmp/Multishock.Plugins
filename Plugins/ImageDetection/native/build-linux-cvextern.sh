#!/usr/bin/env bash
# Builds native/linux-x64/libcvextern.so: Emgu CV's native wrapper with only the OpenCV modules
# Image Detection uses (core, imgproc, imgcodecs), linked statically against its dependencies.
#
# It runs in the manylinux_2_28 image, so the result needs nothing but glibc 2.27+ and loads on
# any current distribution. Emgu's published Ubuntu runtime instead needs ~50 system libraries
# at Ubuntu 24.04's exact versions.
#
# Requires Docker. The source checkout and build tree are kept in the "multishock-cvextern"
# volume, so rebuilds are incremental.
set -euo pipefail

EMGU_TAG=4.12.0
IMAGE=quay.io/pypa/manylinux_2_28_x86_64
OUTPUT_DIR="$(cd "$(dirname "$0")" && pwd)/linux-x64"
mkdir -p "$OUTPUT_DIR"

docker run --rm -i \
  -e EMGU_TAG="$EMGU_TAG" \
  -v multishock-cvextern:/work \
  -v "$OUTPUT_DIR:/out" \
  "$IMAGE" bash -s <<'BUILD'
set -euo pipefail
# Emgu's CMake refuses to configure without a .NET SDK, even though only the native target is built.
command -v dotnet >/dev/null || dnf install -y -q dotnet-sdk-8.0 >/dev/null
cd /work
if [ ! -d emgucv/.git ]; then
  git clone --depth 1 --branch "$EMGU_TAG" https://github.com/emgucv/emgucv.git
  git -C emgucv submodule update --init --depth 1 opencv
fi
mkdir -p build && cd build
cmake ../emgucv \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=OFF \
  -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
  -DCMAKE_CXX_STANDARD=17 \
  -DEMGU_CV_WITH_TESSERACT=OFF -DEMGU_CV_WITH_FREETYPE=OFF -DEMGU_CV_WITH_DEPTHAI=OFF -DEMGU_CV_WITH_TIFF=OFF \
  -DBUILD_LIST=core,imgproc,imgcodecs \
  -DWITH_GTK=OFF -DWITH_QT=OFF -DWITH_GSTREAMER=OFF -DWITH_FFMPEG=OFF -DWITH_V4L=OFF -DWITH_1394=OFF \
  -DWITH_OPENCL=OFF -DWITH_VA=OFF -DWITH_VA_INTEL=OFF -DWITH_OPENGL=OFF -DWITH_VTK=OFF \
  -DWITH_EIGEN=OFF -DWITH_LAPACK=OFF -DWITH_GDAL=OFF -DWITH_OPENEXR=OFF -DWITH_OPENJPEG=OFF \
  -DWITH_JASPER=OFF -DWITH_TIFF=OFF -DWITH_WEBP=OFF -DWITH_AVIF=OFF -DWITH_PROTOBUF=OFF \
  -DWITH_ITT=OFF -DWITH_TBB=OFF -DWITH_OPENMP=OFF -DWITH_CUDA=OFF \
  -DWITH_PNG=ON -DBUILD_PNG=ON -DWITH_JPEG=ON -DBUILD_JPEG=ON -DBUILD_ZLIB=ON \
  -DBUILD_TESTS=OFF -DBUILD_PERF_TESTS=OFF -DBUILD_opencv_apps=OFF -DBUILD_DOCS=OFF \
  -DBUILD_JAVA=OFF -DBUILD_opencv_python3=OFF \
  -DCMAKE_SHARED_LINKER_FLAGS="-static-libstdc++ -static-libgcc -Wl,--exclude-libs,ALL" \
  > /work/configure.log
cmake --build . --target cvextern --parallel "$(nproc)" > /work/build.log 2>&1 || { tail -40 /work/build.log; exit 1; }
cp /work/emgucv/libs/libcvextern.so /out/libcvextern.so
strip --strip-unneeded /out/libcvextern.so
echo "Needs:"; objdump -p /out/libcvextern.so | grep NEEDED
BUILD

echo "Built $OUTPUT_DIR/libcvextern.so"
