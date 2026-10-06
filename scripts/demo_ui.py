#!/usr/bin/env python3
"""Headed acceptance demo for AeroForge Studio."""
from __future__ import annotations

import sys
import time
from pathlib import Path

from playwright.sync_api import sync_playwright

OUT = Path("/opt/cursor/artifacts")
OUT.mkdir(parents=True, exist_ok=True)
URL = "http://127.0.0.1:43123"


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch(
            channel="chrome",
            headless=False,
            args=["--window-size=1440,900", "--disable-dev-shm-usage"],
        )
        page = browser.new_page(viewport={"width": 1440, "height": 900})
        page.goto(URL, wait_until="networkidle", timeout=60_000)
        page.wait_for_timeout(1500)
        page.screenshot(path=str(OUT / "aeroforge_studio_idle.png"), full_page=False)

        btn = page.get_by_role("button", name="Forge Design")
        btn.click()
        page.wait_for_timeout(800)
        page.screenshot(path=str(OUT / "aeroforge_compiling.png"), full_page=False)

        # Wait for completion: Download button enabled or agent chat with Kernel
        deadline = time.time() + 120
        ok = False
        last_err = ""
        while time.time() < deadline:
            # error state
            err = page.locator("text=Error:").first
            if err.count() and err.is_visible():
                last_err = err.inner_text()
                page.screenshot(path=str(OUT / "aeroforge_error.png"), full_page=False)
                break
            download = page.get_by_role("button", name="Download 3D-printable STL")
            if download.count() and download.is_enabled():
                ok = True
                break
            page.wait_for_timeout(1500)

        page.screenshot(path=str(OUT / "aeroforge_after_forge.png"), full_page=False)

        if ok:
            # Orbit gesture on canvas
            canvas = page.locator("canvas").first
            box = canvas.bounding_box()
            if box:
                x = box["x"] + box["width"] * 0.55
                y = box["y"] + box["height"] * 0.45
                page.mouse.move(x, y)
                page.mouse.down()
                page.mouse.move(x + 80, y + 20, steps=12)
                page.mouse.up()
            page.get_by_role("button", name="wireframe").click()
            page.wait_for_timeout(600)
            page.get_by_role("button", name="solid").click()
            page.wait_for_timeout(600)
            page.screenshot(path=str(OUT / "aeroforge_mesh_ready.png"), full_page=False)

        browser.close()
        print("OK" if ok else f"FAIL:{last_err}")
        return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
