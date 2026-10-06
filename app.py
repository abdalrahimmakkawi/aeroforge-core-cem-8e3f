"""NeuralOS dashboard: size a chamber, see the heat map, export a printable solid."""

from __future__ import annotations

from lazyimp import lazy

sys = (lazy)("sys")
Path = (lazy)("pathlib", "Path")

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

np = (lazy)("numpy")
go = (lazy)("plotly.graph_objects")
st = (lazy)("streamlit")
make_subplots = (lazy)("plotly.subplots", "make_subplots")

build_display = (lazy)("cad_engine.meshbuild", "build_display")
evaluate = (lazy)("engine_math.design", "evaluate")
export_engine = (lazy)("cad_engine.generator", "export_engine")
render_station_png = (lazy)("engine_math.figures", "render_station_png")
AMBIENTS = (lazy)("engine_math.presets", "AMBIENTS")
MATERIALS = (lazy)("engine_math.presets", "MATERIALS")
PROPELLANTS = (lazy)("engine_math.presets", "PROPELLANTS")

st.set_page_config(
    page_title="NeuralOS — Generative Propulsion",
    page_icon="◈",
    layout="wide",
    initial_sidebar_state="expanded",
)

THERMAL_SCALE = [
    [0.00, "#071522"],
    [0.16, "#0e3d63"],
    [0.34, "#14918c"],
    [0.50, "#e0b15a"],
    [0.68, "#e25b2a"],
    [0.86, "#ffd4a8"],
    [1.00, "#fff8f1"],
]

st.markdown(
    """
    <style>
    @import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=IBM+Plex+Sans:wght@400;500;600&display=swap');
    html, body, [class*="css"] { font-family: "IBM Plex Sans", sans-serif; }
    .block-container { padding-top: 1.1rem; max-width: 1320px; }
    .brand { font-weight: 600; letter-spacing: 0.18em; font-size: 1.45rem; color: #f3f6fb; margin: 0; }
    .brandsub { color: #9aa6b8; font-size: 0.98rem; margin: 0.15rem 0 0.2rem; }
    .pill {
        font-family: "IBM Plex Mono", monospace; font-size: 0.72rem; letter-spacing: 0.08em;
        color: #e3943f; border: 1px solid #4a3424; background: #1c140f;
        padding: 0.35rem 0.65rem; border-radius: 999px; white-space: nowrap;
    }
    .topbar { display: flex; justify-content: space-between; align-items: flex-end; gap: 1rem; }
    .statusline { font-family: "IBM Plex Mono", monospace; color: #b7c2d2; font-size: 0.82rem; margin: 0.35rem 0 0.8rem; }
    div[data-testid="stMetric"] {
        background: #141922; border: 1px solid #2a3344; border-radius: 12px;
        padding: 0.55rem 0.75rem 0.35rem;
    }
    div[data-testid="stMetricValue"] { font-family: "IBM Plex Mono", monospace; font-size: 1.2rem; }
    div[data-testid="stMetricLabel"] { color: #9aa6b8; }
    .note { color: #8e9aab; font-size: 0.84rem; line-height: 1.45; }
    footer { visibility: hidden; }
    </style>
    """,
    unsafe_allow_html=True,
)


def _apply_preset(name: str) -> None:
    gas = PROPELLANTS[name]
    st.session_state["gamma"] = float(gas["gamma"])
    st.session_state["chamber_temp_k"] = float(gas["chamber_temp_k"])
    st.session_state["molar_mass"] = float(gas["molar_mass"])
    st.session_state["of_ratio"] = float(gas["of_ratio"])


if "preset" not in st.session_state:
    st.session_state["preset"] = "lox_methane"
    _apply_preset("lox_methane")
    st.session_state["_preset_applied"] = "lox_methane"


def _freeze(inputs: dict) -> tuple:
    frozen = []
    for key in sorted(inputs):
        value = inputs[key]
        if isinstance(value, float):
            value = round(value, 6)
        frozen.append((key, value))
    return tuple(frozen)


@st.cache_data(show_spinner=False)
def cached_view(key: tuple, mode: str) -> dict:
    design = evaluate(dict(key))
    nozzle = design["nozzle"]
    thermal = design["thermal"]
    wall = design["wall"]
    gas = design["gas"]
    traces = build_display(design, mode)
    packed = []
    for trace in traces:
        packed.append(
            {
                "x": trace["vertices"][:, 0],
                "y": trace["vertices"][:, 1],
                "z": trace["vertices"][:, 2],
                "i": trace["faces"][:, 0],
                "j": trace["faces"][:, 1],
                "k": trace["faces"][:, 2],
                "intensity": trace["intensity"],
                "name": trace["name"],
                "opacity": trace["opacity"],
                "legend": trace.get("legend", True),
            }
        )
    return {
        "traces": packed,
        "warnings": list(design["warnings"]),
        "material": design["material"]["label"],
        "t_max": float(design["material"]["t_max_k"]),
        "mass_kg": float(design["mass_kg"]),
        "volume_cm3": float(design["volume_m3"] * 1e6),
        "helix_turns": float(design["helix_turns"]),
        "z_mm": gas.z_m * 1000.0,
        "r_gas_mm": wall.r_gas_m * 1000.0,
        "r_outer_mm": wall.r_jacket_outer_m * 1000.0,
        "r_channel_mm": wall.r_channel_m * 1000.0,
        "throat_z_mm": float(gas.throat_z_m * 1000.0),
        "q_mw": thermal.heat_flux_w_m2 / 1e6,
        "tw": thermal.wall_temp_k,
        "tc": thermal.coolant_temp_k,
        "spec": {
            "isp_s": nozzle["isp_s"],
            "isp_sl_s": nozzle["isp_sea_level_s"],
            "epsilon": nozzle["expansion_ratio"],
            "throat_mm": nozzle["throat_radius_mm"] * 2.0,
            "exit_mm": nozzle["exit_radius_mm"] * 2.0,
            "chamber_mm": nozzle["chamber_radius_mm"] * 2.0,
            "chamber_length_mm": nozzle["chamber_length_mm"],
            "nozzle_length_mm": nozzle["nozzle_length_mm"],
            "c_star": nozzle["c_star_m_s"],
            "cf": nozzle["cf"],
            "mdot": nozzle["mass_flow_kg_s"],
            "exit_mach": nozzle["exit_mach"],
            "lstar": nozzle["lstar_actual_m"],
            "adapted": nozzle["adapted"],
            "pe_bar": nozzle["exit_pressure_pa"] / 1e5,
            "entrance_deg": nozzle["entrance_angle_deg"],
            "bell": nozzle["bell_fraction_actual"],
            "peak_q": thermal.peak_heat_flux_w_m2 / 1e6,
            "peak_tw": thermal.peak_wall_temp_k,
            "coolant_dt": thermal.coolant_delta_t_k,
            "coolant_dp_bar": thermal.coolant_delta_p_pa / 1e5,
            "coolant_v": thermal.coolant_velocity_m_s,
            "coolant_mdot": thermal.coolant_mass_flow_kg_s,
            "channel_mm": thermal.channel_diameter_m * 1000.0,
            "land_mm": wall.min_land_m * 1000.0,
            "h_gas_peak": float(np.max(thermal.h_gas)),
            "h_cool": thermal.h_coolant,
        },
    }


def _figure_3d(payload: dict) -> go.Figure:
    intensities = np.concatenate([trace["intensity"] for trace in payload["traces"]])
    cmin = float(np.min(intensities))
    cmax = float(np.max(intensities))
    if cmax - cmin < 5.0:
        cmax = cmin + 5.0
    fig = go.Figure()
    seen = set()
    for trace in payload["traces"]:
        show = trace["legend"] and trace["name"] not in seen
        seen.add(trace["name"])
        fig.add_trace(
            go.Mesh3d(
                x=trace["x"],
                y=trace["y"],
                z=trace["z"],
                i=trace["i"],
                j=trace["j"],
                k=trace["k"],
                intensity=trace["intensity"],
                intensitymode="vertex",
                colorscale=THERMAL_SCALE,
                cmin=cmin,
                cmax=cmax,
                showscale=show and trace["name"] in {"Hot wall", "Closeout", "Gas wall"},
                colorbar=dict(title="K", thickness=14, len=0.7, tickfont=dict(color="#c5ccd6")),
                opacity=trace["opacity"],
                name=trace["name"],
                showlegend=show,
                hovertemplate=f"{trace['name']}<br>%{{intensity:.0f}} K<extra></extra>",
                lighting=dict(ambient=0.48, diffuse=0.72, specular=0.28, roughness=0.55, fresnel=0.15),
                flatshading=False,
            )
        )
    # Only one colorbar: hide the rest explicitly by keeping the first hot surface.
    scales = [i for i, trace in enumerate(fig.data) if trace.showscale]
    for index in scales[1:]:
        fig.data[index].showscale = False
    fig.update_layout(
        paper_bgcolor="rgba(0,0,0,0)",
        margin=dict(l=0, r=0, t=0, b=0),
        height=680,
        uirevision="neuralos-camera-2",
        legend=dict(orientation="h", yanchor="bottom", y=1.01, x=0, font=dict(color="#d5dbe6")),
        scene=dict(
            aspectmode="data",
            bgcolor="rgba(0,0,0,0)",
            xaxis=dict(visible=False),
            yaxis=dict(visible=False),
            zaxis=dict(visible=False),
            camera=dict(eye=dict(x=2.35, y=1.55, z=1.05), up=dict(x=0, y=0, z=1)),
        ),
    )
    return fig


def _axis_style(title: str) -> dict:
    return dict(title=title, gridcolor="#243041", zerolinecolor="#243041", color="#c5ccd6")


def _contour_figure(payload: dict) -> go.Figure:
    z = payload["z_mm"]
    fig = go.Figure()
    fig.add_trace(
        go.Scatter(
            x=np.concatenate([z, z[::-1]]),
            y=np.concatenate([payload["r_outer_mm"], payload["r_gas_mm"][::-1]]),
            fill="toself",
            fillcolor="rgba(42, 51, 66, 0.9)",
            line=dict(width=0),
            name="Metal",
            hoverinfo="skip",
        )
    )
    fig.add_trace(
        go.Scatter(x=z, y=payload["r_outer_mm"], line=dict(color="#e3943f", width=2), name="Outer wall")
    )
    fig.add_trace(go.Scatter(x=z, y=payload["r_gas_mm"], line=dict(color="#e7ecf3", width=2), name="Gas wall"))
    fig.add_trace(
        go.Scatter(
            x=z,
            y=payload["r_channel_mm"],
            line=dict(color="#7ec8c3", width=1.4, dash="dash"),
            name="Channel center",
        )
    )
    fig.add_vline(x=payload["throat_z_mm"], line_width=1, line_dash="dot", line_color="#8b95a7")
    fig.update_layout(
        paper_bgcolor="rgba(0,0,0,0)",
        plot_bgcolor="rgba(0,0,0,0)",
        height=360,
        margin=dict(l=48, r=16, t=28, b=40),
        legend=dict(orientation="h", y=1.14, font=dict(color="#d5dbe6")),
        xaxis=_axis_style("Axial station (mm)"),
        yaxis=_axis_style("Radius (mm)"),
        font=dict(color="#c5ccd6"),
    )
    return fig


def _thermal_figure(payload: dict) -> go.Figure:
    z = payload["z_mm"]
    fig = make_subplots(specs=[[{"secondary_y": True}]])
    fig.add_trace(
        go.Scatter(x=z, y=payload["tw"], name="Gas-side wall", line=dict(color="#e25b2a", width=2)),
        secondary_y=False,
    )
    fig.add_trace(
        go.Scatter(x=z, y=payload["tc"], name="Coolant", line=dict(color="#7ec8c3", width=2)),
        secondary_y=False,
    )
    fig.add_trace(
        go.Scatter(x=z, y=payload["q_mw"], name="Heat flux", line=dict(color="#e0b15a", width=1.6, dash="dot")),
        secondary_y=True,
    )
    fig.add_vline(x=payload["throat_z_mm"], line_width=1, line_dash="dot", line_color="#8b95a7")
    fig.update_layout(
        paper_bgcolor="rgba(0,0,0,0)",
        plot_bgcolor="rgba(0,0,0,0)",
        height=360,
        margin=dict(l=48, r=48, t=28, b=40),
        legend=dict(orientation="h", y=1.14, font=dict(color="#d5dbe6")),
        font=dict(color="#c5ccd6"),
    )
    fig.update_xaxes(**_axis_style("Axial station (mm)"))
    fig.update_yaxes(title_text="Temperature (K)", secondary_y=False, gridcolor="#243041", color="#c5ccd6")
    fig.update_yaxes(title_text="Heat flux (MW/m²)", secondary_y=True, gridcolor="#243041", color="#e0b15a")
    return fig


with st.sidebar:
    st.markdown("### Flight point")
    st.selectbox(
        "Propellant",
        list(PROPELLANTS),
        format_func=lambda key: PROPELLANTS[key]["label"],
        key="preset",
    )
    if st.session_state["preset"] != st.session_state.get("_preset_applied"):
        _apply_preset(st.session_state["preset"])
        st.session_state["_preset_applied"] = st.session_state["preset"]
        st.rerun()

    prop = PROPELLANTS[st.session_state["preset"]]
    st.caption(f"Coolant is {prop['coolant_name']}, the fuel stream in regenerative cooling.")
    thrust_kn = st.slider("Target thrust (kN)", 5.0, 120.0, 25.0, 1.0)
    chamber_mpa = st.slider("Chamber pressure (MPa)", 1.0, 12.0, 3.5, 0.1)
    ambient_key = st.selectbox(
        "Ambient",
        list(AMBIENTS),
        format_func=lambda key: AMBIENTS[key]["label"],
    )
    design_eps = None
    if ambient_key == "vacuum":
        design_eps = st.slider("Design expansion ratio", 6.0, 60.0, 25.0, 1.0)

    st.markdown("### Gas")
    gamma = st.number_input("Specific heat ratio", min_value=1.12, max_value=1.40, step=0.01, key="gamma")
    chamber_temp = st.number_input("Chamber temperature (K)", min_value=2000.0, max_value=4500.0, step=10.0, key="chamber_temp_k")
    molar = st.number_input("Molar mass (kg/kmol)", min_value=8.0, max_value=32.0, step=0.1, key="molar_mass")
    of_ratio = st.number_input("Oxidizer / fuel", min_value=1.0, max_value=8.0, step=0.1, key="of_ratio")

    st.markdown("### Contour")
    radius_ratio = st.slider("Chamber / throat radius", 1.6, 3.2, 2.5, 0.1)
    lstar = st.slider("Characteristic length L* (m)", 0.6, 1.6, 1.0, 0.05)
    bell = st.slider("Bell length / 15° cone", 0.65, 1.05, 0.80, 0.05)
    exit_angle = st.slider("Exit half-angle (deg)", 6.0, 16.0, 10.0, 0.5)

    st.markdown("### Cooling")
    material_key = st.selectbox(
        "Liner alloy",
        list(MATERIALS),
        format_func=lambda key: MATERIALS[key]["label"],
    )
    st.caption(MATERIALS[material_key]["note"])
    channel_count = st.slider("Channel count", 8, 48, 24, 1)
    helix_turns = st.slider("Helix turns", 0.0, 3.0, 0.0, 0.25)
    hot_wall_mm = st.slider("Hot-wall thickness (mm)", 0.6, 2.4, 0.7, 0.1)
    auto_diameter = st.checkbox("Size passages from a velocity target", value=True)
    channel_mm = None
    coolant_velocity = 38.0
    if auto_diameter:
        coolant_velocity = st.slider("Coolant velocity target (m/s)", 12.0, 50.0, 38.0, 1.0)
    else:
        channel_mm = st.slider("Channel diameter (mm)", 1.5, 8.0, 2.4, 0.1)

    st.markdown("### View")
    view_mode = st.radio("Viewport", ["Cutaway", "Exterior"], horizontal=True)


inputs = {
    "thrust_n": thrust_kn * 1000.0,
    "chamber_pressure_pa": chamber_mpa * 1e6,
    "ambient_pressure_pa": float(AMBIENTS[ambient_key]["pressure_pa"]),
    "gamma": float(gamma),
    "chamber_temp_k": float(chamber_temp),
    "molar_mass": float(molar),
    "of_ratio": float(of_ratio),
    "radius_ratio": float(radius_ratio),
    "lstar_m": float(lstar),
    "exit_half_angle_deg": float(exit_angle),
    "bell_fraction": float(bell),
    "design_expansion_ratio": None if design_eps is None else float(design_eps),
    "channel_count": int(channel_count),
    "helix_turns": float(helix_turns),
    "hot_wall_m": float(hot_wall_mm) / 1000.0,
    "channel_diameter_m": None if channel_mm is None else float(channel_mm) / 1000.0,
    "coolant_velocity_m_s": float(coolant_velocity),
    "coolant_rho": float(prop["coolant_rho"]),
    "coolant_cp": float(prop["coolant_cp"]),
    "coolant_k": float(prop["coolant_k"]),
    "coolant_mu": float(prop["coolant_mu"]),
    "coolant_pr": float(prop["coolant_pr"]),
    "coolant_inlet_k": float(prop["coolant_inlet_k"]),
    "material_key": material_key,
}
mode = "cutaway" if view_mode == "Cutaway" else "exterior"

st.markdown(
    """
    <div class="topbar">
      <div>
        <p class="brand">NEURALOS</p>
        <p class="brandsub">Chamber, nozzle, and regenerative cooling from thrust and the gas model.</p>
      </div>
      <div class="pill">IDEAL GAS · BARTZ · PARAMETRIC SOLID</div>
    </div>
    """,
    unsafe_allow_html=True,
)

try:
    payload = cached_view(_freeze(inputs), mode)
except ValueError as exc:
    st.error(str(exc))
    st.stop()

spec = payload["spec"]
regime = "Adapted to ambient" if spec["adapted"] else "Underexpanded"
st.markdown(
    f"<p class='statusline'>{prop['label']} · {regime} · ε {spec['epsilon']:.2f} · "
    f"exit Mach {spec['exit_mach']:.2f} · {payload['material']} liner · "
    f"{channel_count} × {spec['channel_mm']:.2f} mm passages</p>",
    unsafe_allow_html=True,
)

m1, m2, m3 = st.columns(3)
m4, m5, m6 = st.columns(3)
m1.metric("Specific impulse", f"{spec['isp_s']:.0f} s", delta=f"{spec['isp_sl_s']:.0f} s at sea level")
m2.metric("Expansion ratio", f"{spec['epsilon']:.2f}")
m3.metric("Throat diameter", f"{spec['throat_mm']:.1f} mm")
m4.metric("Peak heat flux", f"{spec['peak_q']:.1f} MW/m²")
m5.metric(
    "Peak wall temperature",
    f"{spec['peak_tw']:.0f} K",
    delta=f"{spec['peak_tw'] - payload['t_max']:.0f} K vs limit",
    delta_color="inverse",
)
m6.metric("Coolant temperature rise", f"{spec['coolant_dt']:.0f} K")

if payload["warnings"]:
    for warning in payload["warnings"]:
        st.warning(warning)
else:
    st.success("Wall temperature is inside the liner limit at this firing point.")

st.plotly_chart(_figure_3d(payload), use_container_width=True)
if mode == "cutaway":
    st.markdown(
        "<p class='note'>Cutaway looks onto the +X window. The hot wall is gas-side temperature, "
        "the passages carry coolant temperature from the exit manifold toward the injector, "
        "and the closeout runs near the coolant. Drag to orbit. Units on the mesh are millimeters.</p>",
        unsafe_allow_html=True,
    )
else:
    st.markdown(
        "<p class='note'>Exterior shell is painted with gas-side wall temperature so the throat "
        "load is visible on the outside of the part. The bore shows the same temperature. "
        "Drag to orbit. Units are millimeters.</p>",
        unsafe_allow_html=True,
    )

left, right = st.columns(2)
with left:
    st.markdown("**Meridional contour**")
    st.plotly_chart(_contour_figure(payload), use_container_width=True)
with right:
    st.markdown("**Thermal load**")
    st.plotly_chart(_thermal_figure(payload), use_container_width=True)

c1, c2, c3 = st.columns(3)
with c1:
    st.markdown(
        f"""**Geometry**
- Chamber Ø {spec['chamber_mm']:.1f} mm, length {spec['chamber_length_mm']:.0f} mm
- Nozzle length {spec['nozzle_length_mm']:.0f} mm, exit Ø {spec['exit_mm']:.1f} mm
- L* {spec['lstar']:.2f} m · bell {spec['bell']:.2f} · entrance {spec['entrance_deg']:.1f}°
- Exit pressure {spec['pe_bar']:.3f} bar"""
    )
with c2:
    st.markdown(
        f"""**Performance**
- c* {spec['c_star']:.0f} m/s · Cf {spec['cf']:.3f}
- Mass flow {spec['mdot']:.2f} kg/s
- Fuel coolant {spec['coolant_mdot']:.2f} kg/s at {spec['coolant_v']:.1f} m/s
- Coolant ΔP ≈ {spec['coolant_dp_bar']:.2f} bar (rough wall)"""
    )
with c3:
    st.markdown(
        f"""**Hardware**
- Land at throat {spec['land_mm']:.2f} mm
- Helix {payload['helix_turns']:.2f} turns
- Metal volume ≈ {payload['volume_cm3']:.0f} cm³ · {payload['mass_kg']:.2f} kg
- Peak gas-side h {spec['h_gas_peak']/1000:.1f} kW/m²K"""
    )

st.markdown("### Printable solid")
st.markdown(
    "<p class='note'>The viewport is a lightweight cutaway. The STL is a watertight solid "
    "with the cooling passages subtracted, in millimeters, suitable for an LPBF build or a "
    "sectioned demo print. STEP is the CadQuery solid of the same wall.</p>",
    unsafe_allow_html=True,
)
cut_step = st.checkbox(
    "Cut cooling passages into the STEP file",
    value=False,
    help="Swept cuts in the CAD kernel are much slower than the mesh boolean. Leave this off unless you need the voids in STEP.",
)

export_key = _freeze(inputs) + (("step_channels", bool(cut_step)),)


@st.cache_data(show_spinner="Cutting cooling passages and writing STL / STEP…")
def cached_export(key: tuple) -> dict:
    step_channels = dict(key).pop("step_channels")
    flight = {k: v for k, v in key if k != "step_channels"}
    bundle = export_engine(flight, cut_step_channels=bool(step_channels))
    design = evaluate(flight)
    png_path = ROOT / "outputs" / "contour.png"
    render_station_png(design, png_path)
    bundle["png_bytes"] = png_path.read_bytes()
    z = design["gas"].z_m
    lines = ["z_m,r_gas_m,mach,heat_flux_w_m2,wall_temp_k,coolant_temp_k"]
    thermal = design["thermal"]
    for i in range(len(z)):
        lines.append(
            f"{z[i]:.6f},{design['wall'].r_gas_m[i]:.6f},{thermal.mach[i]:.5f},"
            f"{thermal.heat_flux_w_m2[i]:.4f},{thermal.wall_temp_k[i]:.3f},{thermal.coolant_temp_k[i]:.3f}"
        )
    bundle["csv_text"] = "\n".join(lines)
    return bundle


if st.button("Build 3D-printable mesh", type="primary"):
    st.session_state["export_key"] = export_key

if st.session_state.get("export_key") == export_key:
    bundle = cached_export(export_key)
    state = "watertight" if bundle["watertight"] else "open mesh"
    st.markdown(
        f"<p class='note'>{bundle['triangles']:,} triangles · {bundle['volume_cm3']:.1f} cm³ mesh volume · {state}. "
        f"{bundle['step_note']}</p>",
        unsafe_allow_html=True,
    )
    d1, d2, d3, d4 = st.columns(4)
    d1.download_button(
        "STL",
        data=bundle["stl_bytes"],
        file_name="neuralos_engine.stl",
        mime="model/stl",
        use_container_width=True,
    )
    if bundle["step_bytes"]:
        d2.download_button(
            "STEP",
            data=bundle["step_bytes"],
            file_name="neuralos_engine.step",
            mime="application/step",
            use_container_width=True,
        )
    else:
        d2.caption(bundle["step_note"])
    d3.download_button(
        "Stations CSV",
        data=bundle["csv_text"],
        file_name="neuralos_stations.csv",
        mime="text/csv",
        use_container_width=True,
    )
    d4.download_button(
        "Contour PNG",
        data=bundle["png_bytes"],
        file_name="neuralos_contour.png",
        mime="image/png",
        use_container_width=True,
    )
elif st.session_state.get("export_key"):
    st.info("The flight point changed. Build the mesh again to refresh the files.")

with st.expander("What this model is, and what it is not"):
    st.markdown(
        """
        Throat area comes from `F = Cf · Pc · At`, with Cf from the isentropic thrust
        coefficient at the design exit pressure. The contour is a circular-arc throat
        and a parabolic bell sized to a fraction of a 15° cone. Heat flux is a Bartz
        estimate with gas properties at chamber stagnation; the coolant march is
        Dittus–Boelter in circular passages fed by the fuel flow.

        Preset gas properties are frozen-γ idealizations, not equilibrium combustion.
        PicoGK (LEAP 71) is the voxel kernel to reach for when this wall-and-tube
        model is no longer enough. This dashboard stays in CadQuery and Trimesh so
        a chamber can be resized and exported without leaving Python.
        """
    )
