"""Regression tests for CEF C API function-pointer ordering."""

import contextlib
import io
import pathlib
import re
import unittest

from cef_parser import obj_header
from make_interop import get_funcs


ROOT = pathlib.Path(__file__).resolve().parent
INCLUDE = ROOT / "include"
GENERATED = ROOT.parent / "CefGlue" / "Interop" / "Classes.g"


class VersionedLayoutTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.header = obj_header()
        cls.header.add_directory(
            str(INCLUDE),
            ["cef_application_mac.h", "cef_version.h", "cef_thread.h", "cef_waitable_event.h"],
        )

    def check_layout(self, class_name, capi_name, last_fields):
        # Compare the generator's output to the checked-in file as well as to
        # the expected C API tail, where versioned methods are appended.
        with contextlib.redirect_stdout(io.StringIO()):
            fields = [
                func["field_name"]
                for func in get_funcs(self.header.get_class(class_name))
                if not func["basefunc"]
            ]

        source = (GENERATED / f"{capi_name}.g.cs").read_text(encoding="utf-8")
        generated_fields = re.findall(r"internal IntPtr (_[a-z0-9_]+);", source)
        self.assertEqual(fields, generated_fields)
        self.assertEqual(last_fields, fields[-len(last_fields):])

    def test_download_item(self):
        self.check_layout(
            "CefDownloadItem",
            "cef_download_item_t",
            ["_get_content_disposition", "_get_mime_type", "_is_paused"],
        )

    def test_command_line(self):
        self.check_layout(
            "CefCommandLine",
            "cef_command_line_t",
            ["_append_argument", "_prepend_wrapper", "_remove_switch"],
        )

    def test_request_context(self):
        self.check_layout(
            "CefRequestContext",
            "cef_request_context_t",
            ["_get_chrome_color_scheme_variant", "_add_setting_observer", "_clear_http_cache"],
        )


if __name__ == "__main__":
    unittest.main()
