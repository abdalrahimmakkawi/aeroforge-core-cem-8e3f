"""Static contour figure for slides and the PNG download."""

from __future__ import annotations

from lazyimp import lazy

Path = (lazy)("pathlib", "Path")
matplotlib = (lazy)("matplotlib")
plt = (lazy)("matplotlib.pyplot")


def render_station_png(design: dict, path: str | Path) -> str:
    matplotlib.use("Agg")
    wall = design["wall"]
    thermal = design["thermal"]
    z = wall.z_m * 1000.0
    fig, ax = plt.subplots(figsize=(9.2, 4.2), dpi=140)
    fig.patch.set_facecolor("#0c0e12")
    ax.set_facecolor("#0c0e12")
    ax.fill_between(z, wall.r_gas_m * 1000.0, wall.r_jacket_outer_m * 1000.0, color="#2a3342", linewidth=0)
    ax.plot(z, wall.r_gas_m * 1000.0, color="#e7ecf3", lw=1.4, label="Gas wall")
    ax.plot(z, wall.r_channel_m * 1000.0, color="#7ec8c3", lw=1.0, ls="--", label="Channel center")
    ax.plot(z, wall.r_jacket_outer_m * 1000.0, color="#e3943f", lw=1.2, label="Outer wall")
    ax.axvline(design["gas"].throat_z_m * 1000.0, color="#8b95a7", lw=0.6, ls=":")
    ax.set_xlabel("Axial station (mm)", color="#c5ccd6")
    ax.set_ylabel("Radius (mm)", color="#c5ccd6")
    ax.tick_params(colors="#9aa3b2")
    for spine in ax.spines.values():
        spine.set_color("#2c3442")
    ax2 = ax.twinx()
    ax2.plot(z, thermal.heat_flux_w_m2 / 1e6, color="#e25b2a", lw=1.3, label="Heat flux")
    ax2.set_ylabel("Heat flux (MW/m²)", color="#e25b2a")
    ax2.tick_params(colors="#e25b2a")
    ax2.spines["right"].set_color("#5a3a32")
    handles, labels = ax.get_legend_handles_labels()
    h2, l2 = ax2.get_legend_handles_labels()
    ax.legend(handles + h2, labels + l2, frameon=False, labelcolor="#d5dbe6", fontsize=8, loc="upper left")
    ax.set_title("Meridional contour and Bartz heat flux", color="#e7ecf3", loc="left", fontsize=11)
    fig.tight_layout()
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(path, facecolor=fig.get_facecolor())
    plt.close(fig)
    return str(path)
