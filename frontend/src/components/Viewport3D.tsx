"use client";

import { Canvas, useThree } from "@react-three/fiber";
import { Bounds, Center, Environment, OrbitControls } from "@react-three/drei";
import { Suspense, useEffect, useMemo, useState } from "react";
import * as THREE from "three";
import { STLLoader } from "three/examples/jsm/loaders/STLLoader.js";

type ViewMode = "solid" | "wireframe" | "thermal";

function EnableClipping() {
  const { gl } = useThree();
  useEffect(() => {
    gl.localClippingEnabled = true;
  }, [gl]);
  return null;
}

function StlMesh({
  url,
  mode,
  sliceZ,
  onMetrics,
}: {
  url: string;
  mode: ViewMode;
  sliceZ: number;
  onMetrics?: (m: { bbox: THREE.Vector3; triangles: number }) => void;
}) {
  const [geom, setGeom] = useState<THREE.BufferGeometry | null>(null);

  useEffect(() => {
    let alive = true;
    const loader = new STLLoader();
    loader.load(
      url,
      (g) => {
        if (!alive) return;
        g.computeVertexNormals();
        g.computeBoundingBox();
        g.center();
        g.computeBoundingBox();
        setGeom(g);
        if (onMetrics && g.boundingBox) {
          const size = new THREE.Vector3();
          g.boundingBox.getSize(size);
          const idx = g.index;
          const tris = idx ? idx.count / 3 : g.attributes.position.count / 3;
          onMetrics({ bbox: size, triangles: Math.round(tris) });
        }
      },
      undefined,
      () => alive && setGeom(null)
    );
    return () => {
      alive = false;
    };
  }, [url, onMetrics]);

  const clipPlane = useMemo(() => {
    // Park the plane outside the mesh when the slider is near either end.
    if (Math.abs(sliceZ) >= 78) return null;
    return new THREE.Plane(new THREE.Vector3(0, -1, 0), sliceZ);
  }, [sliceZ]);

  const material = useMemo(() => {
    const planes = clipPlane ? [clipPlane] : [];
    if (mode === "wireframe") {
      return new THREE.MeshBasicMaterial({
        color: "#1f4d56",
        wireframe: true,
        transparent: true,
        opacity: 0.85,
        clippingPlanes: planes,
        clipShadows: true,
      });
    }
    if (mode === "thermal") {
      return new THREE.MeshStandardMaterial({
        color: "#ffffff",
        metalness: 0.12,
        roughness: 0.45,
        vertexColors: true,
        emissive: new THREE.Color("#5c2a1a"),
        emissiveIntensity: 0.08,
        clippingPlanes: planes,
        clipShadows: true,
      });
    }
    return new THREE.MeshStandardMaterial({
      color: "#8a9096",
      metalness: 0.78,
      roughness: 0.32,
      envMapIntensity: 0.95,
      side: THREE.DoubleSide,
      clippingPlanes: planes,
      clipShadows: true,
    });
  }, [mode, clipPlane]);

  useEffect(() => {
    if (!geom || mode !== "thermal") return;
    const pos = geom.attributes.position;
    const colors = new Float32Array(pos.count * 3);
    let minY = Infinity;
    let maxY = -Infinity;
    for (let i = 0; i < pos.count; i++) {
      const y = pos.getY(i);
      minY = Math.min(minY, y);
      maxY = Math.max(maxY, y);
    }
    const span = Math.max(maxY - minY, 1e-6);
    const c = new THREE.Color();
    for (let i = 0; i < pos.count; i++) {
      const t = (pos.getY(i) - minY) / span;
      c.setHSL(0.07 - t * 0.07, 0.72, 0.38 + t * 0.28);
      colors[i * 3] = c.r;
      colors[i * 3 + 1] = c.g;
      colors[i * 3 + 2] = c.b;
    }
    geom.setAttribute("color", new THREE.BufferAttribute(colors, 3));
  }, [geom, mode]);

  if (!geom) return null;

  return (
    <Center>
      <mesh geometry={geom} material={material} castShadow receiveShadow />
      {clipPlane && (
        <mesh position={[0, sliceZ, 0]} rotation={[-Math.PI / 2, 0, 0]}>
          <planeGeometry args={[240, 240]} />
          <meshBasicMaterial
            color="#1f4d56"
            transparent
            opacity={0.06}
            side={THREE.DoubleSide}
            depthWrite={false}
          />
        </mesh>
      )}
    </Center>
  );
}

export function Viewport3D({
  stlUrl,
  mode,
  sliceZ,
}: {
  stlUrl: string | null;
  mode: ViewMode;
  sliceZ: number;
}) {
  const [meshInfo, setMeshInfo] = useState<string | null>(null);

  return (
    <div className="relative h-full min-h-[280px] w-full overflow-hidden rounded-md border border-[var(--line)] bg-[#f0ebe3]">
      <Canvas camera={{ position: [180, 120, 180], fov: 42, near: 0.1, far: 5000 }} dpr={[1, 1.75]}>
        <EnableClipping />
        <color attach="background" args={["#f0ebe3"]} />
        <ambientLight intensity={0.72} />
        <directionalLight position={[120, 180, 80]} intensity={1.05} castShadow />
        <directionalLight position={[-100, 40, -60]} intensity={0.28} color="#1f4d56" />
        <Suspense fallback={null}>
          {stlUrl ? (
            <Bounds fit clip observe margin={1.25}>
              <StlMesh
                url={stlUrl}
                mode={mode}
                sliceZ={sliceZ}
                onMetrics={({ bbox, triangles }) =>
                  setMeshInfo(
                    `${bbox.x.toFixed(0)} × ${bbox.y.toFixed(0)} × ${bbox.z.toFixed(0)} mm · ${triangles.toLocaleString()} tris`
                  )
                }
              />
            </Bounds>
          ) : (
            <mesh>
              <icosahedronGeometry args={[26, 1]} />
              <meshStandardMaterial color="#1f4d56" wireframe transparent opacity={0.35} />
            </mesh>
          )}
          <Environment preset="apartment" />
        </Suspense>
        <OrbitControls makeDefault enableDamping dampingFactor={0.08} />
        <gridHelper args={[400, 20, "#c9bfb0", "#ddd5c8"]} position={[0, -80, 0]} />
      </Canvas>
      <div className="pointer-events-none absolute inset-x-0 bottom-0 flex justify-between gap-3 px-3 py-2 text-[11px] font-mono text-[var(--muted)] bg-gradient-to-t from-[#f0ebe3]/95 to-transparent">
        <span>{stlUrl ? meshInfo || "Loading mesh…" : "No mesh yet — run Forge Design"}</span>
        <span>Inconel 718 · 8.19 g/cm³</span>
      </div>
    </div>
  );
}

export type { ViewMode };
