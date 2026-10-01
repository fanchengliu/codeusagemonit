import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import vertShader from './shaders/blackhole.vert.glsl?raw';
import fragShader from './shaders/blackhole.frag.glsl?raw';

/**
 * Geometric units: G = c = 1.
 * Mass M sets the length scale. Event horizon r_s = 2M.
 */
const MASS = 1.0;
const RS = 2.0 * MASS;
const INITIAL_DISTANCE = 28.0 * MASS;

function createStarfieldTexture(size = 1024) {
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size / 2;
  const ctx = canvas.getContext('2d');

  const gradient = ctx.createLinearGradient(0, 0, 0, canvas.height);
  gradient.addColorStop(0, '#02040a');
  gradient.addColorStop(0.45, '#070b18');
  gradient.addColorStop(0.55, '#0a1020');
  gradient.addColorStop(1, '#02040a');
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  // Milky Way band
  for (let i = 0; i < 1800; i++) {
    const x = Math.random() * canvas.width;
    const y = canvas.height * (0.42 + (Math.random() - 0.5) * 0.18);
    const a = 0.04 + Math.random() * 0.08;
    ctx.fillStyle = `rgba(160, 180, 230, ${a})`;
    ctx.beginPath();
    ctx.arc(x, y, 0.6 + Math.random() * 2.2, 0, Math.PI * 2);
    ctx.fill();
  }

  // Stars
  for (let i = 0; i < 6000; i++) {
    const x = Math.random() * canvas.width;
    const y = Math.random() * canvas.height;
    const bright = Math.pow(Math.random(), 4);
    const r = 0.4 + bright * 1.6;
    const warmth = Math.random();
    const cr = 200 + warmth * 55;
    const cg = 200 + (1 - warmth) * 30;
    const cb = 220 + (1 - warmth) * 35;
    ctx.fillStyle = `rgba(${cr | 0}, ${cg | 0}, ${cb | 0}, ${0.35 + bright * 0.65})`;
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fill();
  }

  // Bright reference stars
  for (let i = 0; i < 40; i++) {
    const x = Math.random() * canvas.width;
    const y = Math.random() * canvas.height;
    const g = ctx.createRadialGradient(x, y, 0, x, y, 6);
    g.addColorStop(0, 'rgba(255, 250, 240, 0.95)');
    g.addColorStop(0.4, 'rgba(180, 200, 255, 0.35)');
    g.addColorStop(1, 'rgba(180, 200, 255, 0)');
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(x, y, 6, 0, Math.PI * 2);
    ctx.fill();
  }

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.wrapS = THREE.RepeatWrapping;
  texture.wrapT = THREE.ClampToEdgeWrapping;
  texture.needsUpdate = true;
  return texture;
}

function main() {
  const canvas = document.getElementById('c');
  const loading = document.getElementById('loading');
  const fpsEl = document.getElementById('fps');

  const renderer = new THREE.WebGLRenderer({
    canvas,
    antialias: false,
    powerPreference: 'high-performance',
  });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.75));
  renderer.setSize(window.innerWidth, window.innerHeight, false);
  renderer.outputColorSpace = THREE.SRGBColorSpace;

  const scene = new THREE.Scene();
  // Orthographic full-screen camera for the ray-traced quad
  const screenCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);

  // Virtual camera for OrbitControls — its pose feeds the shader
  const viewCam = new THREE.PerspectiveCamera(
    55,
    window.innerWidth / window.innerHeight,
    0.1,
    1000,
  );
  viewCam.position.set(INITIAL_DISTANCE, INITIAL_DISTANCE * 0.22, 0.01);

  const controls = new OrbitControls(viewCam, canvas);
  controls.enableDamping = true;
  controls.dampingFactor = 0.06;
  controls.minDistance = 8.0 * MASS;
  controls.maxDistance = 80.0 * MASS;
  controls.target.set(0, 0, 0);
  controls.enablePan = true;
  controls.rotateSpeed = 0.7;
  controls.zoomSpeed = 0.9;
  controls.update();

  const starfield = createStarfieldTexture(2048);

  const uniforms = {
    uResolution: { value: new THREE.Vector2(window.innerWidth, window.innerHeight) },
    uTime: { value: 0 },
    uMass: { value: MASS },
    uCameraPos: { value: new THREE.Vector3() },
    uCameraMatrix: { value: new THREE.Matrix3() },
    uFov: { value: THREE.MathUtils.degToRad(viewCam.fov) },
    uDiskInner: { value: 6.0 },   // ISCO = 6M
    uDiskOuter: { value: 28.0 },
    uSteps: { value: 280 },
    uMaxDistance: { value: 120.0 * MASS },
    uStarfield: { value: starfield },
  };

  const material = new THREE.ShaderMaterial({
    vertexShader: vertShader,
    fragmentShader: fragShader,
    uniforms,
    depthWrite: false,
    depthTest: false,
  });

  const quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), material);
  scene.add(quad);

  const camRight = new THREE.Vector3();
  const camUp = new THREE.Vector3();
  const camForward = new THREE.Vector3();

  function syncCameraUniforms() {
    viewCam.updateMatrixWorld();
    uniforms.uCameraPos.value.copy(viewCam.position);

    viewCam.matrixWorld.extractBasis(camRight, camUp, camForward);
    // Shader expects columns = right, up, -forward (look direction)
    const m = uniforms.uCameraMatrix.value;
    m.set(
      camRight.x, camUp.x, -camForward.x,
      camRight.y, camUp.y, -camForward.y,
      camRight.z, camUp.z, -camForward.z,
    );

    uniforms.uFov.value = THREE.MathUtils.degToRad(viewCam.fov);
  }

  function onResize() {
    const w = window.innerWidth;
    const h = window.innerHeight;
    const pr = Math.min(window.devicePixelRatio, 1.75);
    renderer.setPixelRatio(pr);
    renderer.setSize(w, h, false);
    uniforms.uResolution.value.set(w * pr, h * pr);
    viewCam.aspect = w / h;
    viewCam.updateProjectionMatrix();

    // Scale integration steps with resolution for performance
    const pixels = w * h * pr * pr;
    uniforms.uSteps.value = pixels > 2.2e6 ? 200 : pixels > 1.2e6 ? 240 : 300;
  }

  window.addEventListener('resize', onResize);
  onResize();

  let frames = 0;
  let lastFps = performance.now();
  const clock = new THREE.Clock();

  function frame() {
    requestAnimationFrame(frame);
    const t = clock.getElapsedTime();
    uniforms.uTime.value = t;
    controls.update();
    syncCameraUniforms();
    renderer.render(scene, screenCam);

    frames += 1;
    const now = performance.now();
    if (now - lastFps >= 500) {
      const fps = Math.round((frames * 1000) / (now - lastFps));
      fpsEl.textContent = `${fps} FPS · M=${MASS} · rₛ=${RS}`;
      frames = 0;
      lastFps = now;
    }
  }

  // Hide loader once first frame compiles
  renderer.compile(scene, screenCam);
  loading.classList.add('hidden');
  frame();
}

main();
