#!/usr/bin/env python3
"""Headed acceptance demo for redesigned AeroForge Studio."""
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
        page.wait_for_timeout(2000)
        page.screenshot(path=str(OUT / "aeroforge_sidebar_idle.png"), full_page=False)

        overlay = page.locator("[data-nextjs-dialog]")
        if overlay.count() and overlay.first.is_visible():
            page.screenshot(path=str(OUT / "aeroforge_hydration_error.png"), full_page=False)
            browser.close()
            print("FAIL:error_overlay")
            return 1

        page.get_by_role("button", name="Forge design").click()
        page.wait_for_timeout(1000)
        page.screenshot(path=str(OUT / "aeroforge_sidebar_compiling.png"), full_page=False)

        deadline = time.time() + 120
        ok = False
        last_err = ""
        while time.time() < deadline:
            err = page.locator("text=Failed:").first
            if err.count() and err.is_visible():
                last_err = err.inner_text()
                page.screenshot(path=str(OUT / "aeroforge_sidebar_error.png"), full_page=False)
                break
            download = page.get_by_role("button", name="Download STL")
            if download.count() and download.is_enabled():
                ok = True
                break
            page.wait_for_timeout(1500)

        page.screenshot(path=str(OUT / "aeroforge_sidebar_forged.png"), full_page=False)

        if ok:
            canvas = page.locator("canvas").first
            box = canvas.bounding_box()
            if box:
                x = box["x"] + box["width"] * 0.55
                y = box["y"] + box["height"] * 0.45
                page.mouse.move(x, y)
                page.mouse.down()
                page.mouse.move(x + 70, y + 15, steps=10)
                page.mouse.up()
            page.get_by_role("button", name="wireframe").click()
            page.wait_for_timeout(500)
            page.get_by_role("button", name="solid").click()
            page.wait_for_timeout(500)
            page.get_by_role("button", name="LOX / RP-1").click()
            page.wait_for_timeout(300)
            page.get_by_role("button", name="LOX / LCH₄").click()
            page.wait_for_timeout(400)
            page.screenshot(path=str(OUT / "aeroforge_sidebar_ready.png"), full_page=False)
            download = page.get_by_role("button", name="Download STL")
            with page.expect_download(timeout=30_000) as dl_info:
                download.click()
            dl = dl_info.value
            path = OUT / "aeroforge_download.stl"
            dl.save_as(str(path))
            print(f"download_bytes={path.stat().st_size}")

        browser.close()
        print("OK" if ok else f"FAIL:{last_err}")
        return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
