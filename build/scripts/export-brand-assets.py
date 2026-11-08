#!/usr/bin/env python3
"""Export the canonical SVG to the app's PNG, multi-size ICO and Linux icon.

Requires Python 3, Pillow, librsvg 2 and Cairo. Run from any working directory.
"""

import ctypes
import ctypes.util
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "branding/gitgator.svg"
PNG = ROOT / "src/Resources/Images/gitgator.png"
ICO = ROOT / "src/App.ico"
LINUX = ROOT / "packaging/linux/gitgator.png"


def library(name):
    path = ctypes.util.find_library(name)
    if not path:
        raise SystemExit(f"Missing {name}; install librsvg2-2 and libcairo2.")
    return ctypes.CDLL(path)


def render_svg(destination, size):
    rsvg = library("rsvg-2")
    cairo = library("cairo")
    gobject = library("gobject-2.0")
    glib = library("glib-2.0")
    pointer = ctypes.c_void_p

    class Rectangle(ctypes.Structure):
        _fields_ = [(key, ctypes.c_double) for key in ("x", "y", "width", "height")]

    class Error(ctypes.Structure):
        _fields_ = [("domain", ctypes.c_uint), ("code", ctypes.c_int), ("message", ctypes.c_char_p)]

    error_type = ctypes.POINTER(Error)
    rsvg.rsvg_handle_new_from_data.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.POINTER(error_type)]
    rsvg.rsvg_handle_new_from_data.restype = pointer
    rsvg.rsvg_handle_render_document.argtypes = [pointer, pointer, ctypes.POINTER(Rectangle), ctypes.POINTER(error_type)]
    rsvg.rsvg_handle_render_document.restype = ctypes.c_int
    cairo.cairo_image_surface_create.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int]
    cairo.cairo_image_surface_create.restype = pointer
    cairo.cairo_create.argtypes = [pointer]
    cairo.cairo_create.restype = pointer
    cairo.cairo_surface_write_to_png.argtypes = [pointer, ctypes.c_char_p]
    cairo.cairo_surface_write_to_png.restype = ctypes.c_int
    cairo.cairo_destroy.argtypes = [pointer]
    cairo.cairo_surface_destroy.argtypes = [pointer]
    gobject.g_object_unref.argtypes = [pointer]
    glib.g_error_free.argtypes = [error_type]

    def check_error(error):
        if error:
            message = error.contents.message.decode("utf-8")
            glib.g_error_free(error)
            raise RuntimeError(message)

    data = SOURCE.read_bytes()
    error = error_type()
    handle = rsvg.rsvg_handle_new_from_data(data, len(data), ctypes.byref(error))
    check_error(error)
    if not handle:
        raise RuntimeError("Could not load the logo SVG")
    surface = cairo.cairo_image_surface_create(0, size, size)  # CAIRO_FORMAT_ARGB32
    context = cairo.cairo_create(surface)
    try:
        viewport = Rectangle(0, 0, size, size)
        if not rsvg.rsvg_handle_render_document(handle, context, ctypes.byref(viewport), ctypes.byref(error)):
            check_error(error)
            raise RuntimeError("Could not render the logo SVG")
        destination.parent.mkdir(parents=True, exist_ok=True)
        if cairo.cairo_surface_write_to_png(surface, str(destination).encode()) != 0:
            raise RuntimeError(f"Could not write {destination}")
    finally:
        cairo.cairo_destroy(context)
        cairo.cairo_surface_destroy(surface)
        gobject.g_object_unref(handle)


def main():
    render_svg(PNG, 1024)
    with Image.open(PNG) as image:
        image.save(ICO, format="ICO", sizes=[(size, size) for size in (16, 24, 32, 48, 64, 128, 256)])
        LINUX.parent.mkdir(parents=True, exist_ok=True)
        image.resize((512, 512), Image.Resampling.LANCZOS).save(LINUX)
    for path in (PNG, ICO, LINUX):
        print(path.relative_to(ROOT))


if __name__ == "__main__":
    main()
