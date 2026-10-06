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

  const clipPlane = useMemo(
    () => new THREE.Plane(new THREE.Vector3(0, -1, 0), sliceZ),
    [sliceZ]
  );

  const material = useMemo(() => {
    if (mode === "wireframe") {
      return new THREE.MeshBasicMaterial({
        color: "#67e8f9",
        wireframe: true,
        transparent: true,
        opacity: 0.9,
        clippingPlanes: [clipPlane],
        clipShadows: true,
      });
    }
    if (mode === "thermal") {
      return new THREE.MeshStandardMaterial({
        color: "#ffffff",
        metalness: 0.15,
        roughness: 0.4,
        vertexColors: true,
        emissive: new THREE.Color("#7c2d12"),
        emissiveIntensity: 0.12,
        clippingPlanes: [clipPlane],
        clipShadows: true,
      });
    }
    return new THREE.MeshStandardMaterial({
      color: "#94a3b8",
      metalness: 0.85,
      roughness: 0.28,
      envMapIntensity: 1.1,
      side: THREE.DoubleSide,
      clippingPlanes: [clipPlane],
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
      c.setHSL(0.08 - t * 0.08, 0.95, 0.35 + t * 0.35);
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
      <mesh position={[0, sliceZ, 0]} rotation={[-Math.PI / 2, 0, 0]}>
        <planeGeometry args={[240, 240]} />
        <meshBasicMaterial
          color="#22d3ee"
          transparent
          opacity={0.08}
          side={THREE.DoubleSide}
          depthWrite={false}
        />
      </mesh>
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
    <div className="relative h-full w-full rounded-lg overflow-hidden bg-[radial-gradient(ellipse_at_center,_#132033_0%,_#070b12_70%)] border border-slate-800/80">
      <Canvas camera={{ position: [180, 120, 180], fov: 42, near: 0.1, far: 5000 }} dpr={[1, 1.75]}>
        <EnableClipping />
        <color attach="background" args={["#070b12"]} />
        <ambientLight intensity={0.45} />
        <directionalLight position={[120, 180, 80]} intensity={1.25} castShadow />
        <directionalLight position={[-100, 40, -60]} intensity={0.35} color="#67e8f9" />
        <Suspense fallback={null}>
          {stlUrl ? (
            <Bounds fit clip observe margin={1.25}>
              <StlMesh
                url={stlUrl}
                mode={mode}
                sliceZ={sliceZ}
                onMetrics={({ bbox, triangles }) =>
                  setMeshInfo(
                    `${bbox.x.toFixed(0)}×${bbox.y.toFixed(0)}×${bbox.z.toFixed(0)} mm · ${triangles.toLocaleString()} tris`
                  )
                }
              />
            </Bounds>
          ) : (
            <mesh>
              <icosahedronGeometry args={[28, 1]} />
              <meshStandardMaterial color="#164e63" wireframe />
            </mesh>
          )}
          <Environment preset="city" />
        </Suspense>
        <OrbitControls makeDefault enableDamping dampingFactor={0.08} />
        <gridHelper args={[400, 20, "#1e293b", "#0f172a"]} position={[0, -80, 0]} />
      </Canvas>
      <div className="pointer-events-none absolute bottom-2 left-2 right-2 flex justify-between text-[10px] font-mono text-slate-400">
        <span>{stlUrl ? meshInfo || "Loading mesh…" : "Awaiting forge output"}</span>
        <span>Inconel 718 · 8.19 g/cm³</span>
      </div>
    </div>
  );
}

export type { ViewMode };
