/**
 * Schwarzschild null-geodesic ray tracer
 * --------------------------------------
 * Integrates the geodesic equation for light-like worldlines in the
 * Schwarzschild metric (G = c = 1):
 *
 *   ds² = −α dt² + α⁻¹ dr² + r² dθ² + r² sin²θ dφ²
 *   α   = 1 − r_s / r ,   r_s = 2M
 *
 * Conserved quantities along geodesics:
 *   E = α · dt/dλ          (energy at infinity)
 *   L = r² sin²θ · dφ/dλ   (angular momentum)
 *
 * Photon sphere: r_ph = 3M = 1.5 r_s
 * Event horizon: r_h  = 2M = r_s
 * ISCO:          r_isco = 6M
 *
 * Accretion disk uses Novikov–Thorne temperature profile and
 * relativistic Doppler + gravitational redshift for observed color.
 */

precision highp float;

varying vec2 vUv;

uniform vec2  uResolution;
uniform float uTime;
uniform float uMass;          // geometric mass M (r_s = 2M)
uniform vec3  uCameraPos;
uniform mat3  uCameraMatrix;  // columns = right, up, -forward in world
uniform float uFov;
uniform float uDiskInner;     // ISCO radius in units of M
uniform float uDiskOuter;
uniform float uSteps;
uniform float uMaxDistance;
uniform sampler2D uStarfield;

const float PI = 3.141592653589793;
const float TWO_PI = 6.283185307179586;

/* ---------- utility ---------- */

float hash21(vec2 p) {
  p = fract(p * vec2(123.34, 456.21));
  p += dot(p, p + 45.32);
  return fract(p.x * p.y);
}

vec3 blackbody(float t) {
  // Approximate Planck → sRGB (t in Kelvin / 1000 for stability)
  t = clamp(t, 1.0, 40.0);
  float x = t;
  vec3 c;
  c.r = x < 6.6
    ? 1.0
    : clamp(1.292936 * pow(x - 6.0, -0.1332047), 0.0, 1.0);
  c.g = x < 6.6
    ? clamp(0.3900816 * log(x) - 0.6318414, 0.0, 1.0)
    : clamp(1.129783 * pow(x - 6.0, -0.0755148), 0.0, 1.0);
  c.b = x >= 6.6
    ? 1.0
    : (x <= 1.9 ? 0.0 : clamp(0.5431689 * log(x - 1.0) - 0.269876, 0.0, 1.0));
  return c;
}

vec3 tonemapACES(vec3 x) {
  const float a = 2.51;
  const float b = 0.03;
  const float c = 2.43;
  const float d = 0.59;
  const float e = 0.14;
  return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

/* Cartesian ↔ spherical helpers (Schwarzschild spatial slice) */

vec3 cartToSph(vec3 p) {
  float r = length(p);
  float theta = acos(clamp(p.y / max(r, 1e-8), -1.0, 1.0));
  float phi = atan(p.z, p.x);
  return vec3(r, theta, phi);
}

vec3 sphToCart(float r, float theta, float phi) {
  float st = sin(theta);
  return vec3(r * st * cos(phi), r * cos(theta), r * st * sin(phi));
}

/* Convert Cartesian velocity to spherical coordinate velocities */
vec3 cartVelToSph(vec3 p, vec3 v, vec3 sph) {
  float r = sph.x;
  float theta = sph.y;
  float phi = sph.z;
  float st = sin(theta);
  float ct = cos(theta);
  float sp = sin(phi);
  float cp = cos(phi);

  float ur = (p.x * v.x + p.y * v.y + p.z * v.z) / max(r, 1e-8);
  float utheta = (ct * cp * v.x - st * v.y + ct * sp * v.z) / max(r, 1e-8);
  float uphi = (-sp * v.x + cp * v.z) / max(r * st, 1e-8);
  return vec3(ur, utheta, uphi);
}

/* ---------- Schwarzschild geodesic RHS ----------
 * State S = (r, θ, φ, uʳ, uᶿ, uᵠ)
 * Time component uᵗ recovered from null condition g_μν u^μ u^ν = 0:
 *   −α (uᵗ)² + α⁻¹ (uʳ)² + r² (uᶿ)² + r² sin²θ (uᵠ)² = 0
 *   ⇒ (uᵗ)² = α⁻² (uʳ)² + α⁻¹ [ r² (uᶿ)² + r² sin²θ (uᵠ)² ]
 */

struct GeoState {
  float r;
  float theta;
  float phi;
  float ur;
  float utheta;
  float uphi;
};

float alphaOf(float r, float rs) {
  return 1.0 - rs / max(r, rs * 1.001);
}

float utFromNull(GeoState s, float rs) {
  float alpha = alphaOf(s.r, rs);
  float st = max(sin(s.theta), 1e-6);
  float spatial =
      (s.ur * s.ur) / max(alpha, 1e-6)
    + s.r * s.r * (s.utheta * s.utheta + st * st * s.uphi * s.uphi);
  return sqrt(max(spatial / max(alpha, 1e-6), 0.0));
}

GeoState geoDeriv(GeoState s, float rs, float M) {
  float r = max(s.r, rs * 1.001);
  float theta = clamp(s.theta, 1e-4, PI - 1e-4);
  float st = sin(theta);
  float ct = cos(theta);
  float alpha = alphaOf(r, rs);
  float ut = utFromNull(s, rs);

  // Christoffel-symbol geodesic equation for Schwarzschild
  // d²t/dλ² = −(2M)/(r(r−2M)) · uʳ · uᵗ
  // (ut is only needed for radial eq.)
  float d2r =
      M * alpha / (r * r * r) * (ut * ut)
    - M / (r * r * alpha) * (s.ur * s.ur)
    - alpha * r * (s.utheta * s.utheta + st * st * s.uphi * s.uphi);

  float d2theta =
      - (2.0 / r) * s.ur * s.utheta
      + st * ct * s.uphi * s.uphi;

  float d2phi =
      - (2.0 / r) * s.ur * s.uphi
      - 2.0 * ct / max(st, 1e-6) * s.utheta * s.uphi;

  GeoState d;
  d.r = s.ur;
  d.theta = s.utheta;
  d.phi = s.uphi;
  d.ur = d2r;
  d.utheta = d2theta;
  d.uphi = d2phi;
  return d;
}

GeoState addScaled(GeoState a, GeoState b, float k) {
  GeoState o;
  o.r = a.r + k * b.r;
  o.theta = a.theta + k * b.theta;
  o.phi = a.phi + k * b.phi;
  o.ur = a.ur + k * b.ur;
  o.utheta = a.utheta + k * b.utheta;
  o.uphi = a.uphi + k * b.uphi;
  return o;
}

GeoState rk4Step(GeoState s, float h, float rs, float M) {
  GeoState k1 = geoDeriv(s, rs, M);
  GeoState k2 = geoDeriv(addScaled(s, k1, 0.5 * h), rs, M);
  GeoState k3 = geoDeriv(addScaled(s, k2, 0.5 * h), rs, M);
  GeoState k4 = geoDeriv(addScaled(s, k3, h), rs, M);

  GeoState o;
  o.r      = s.r      + (h / 6.0) * (k1.r      + 2.0 * k2.r      + 2.0 * k3.r      + k4.r);
  o.theta  = s.theta  + (h / 6.0) * (k1.theta  + 2.0 * k2.theta  + 2.0 * k3.theta  + k4.theta);
  o.phi    = s.phi    + (h / 6.0) * (k1.phi    + 2.0 * k2.phi    + 2.0 * k3.phi    + k4.phi);
  o.ur     = s.ur     + (h / 6.0) * (k1.ur     + 2.0 * k2.ur     + 2.0 * k3.ur     + k4.ur);
  o.utheta = s.utheta + (h / 6.0) * (k1.utheta + 2.0 * k2.utheta + 2.0 * k3.utheta + k4.utheta);
  o.uphi   = s.uphi   + (h / 6.0) * (k1.uphi   + 2.0 * k2.uphi   + 2.0 * k3.uphi   + k4.uphi);
  return o;
}

/* ---------- accretion disk ---------- */

float diskTemperature(float r, float M, float rin) {
  // Novikov–Thorne / Shakura–Sunyaev T_eff ∝ r^{−3/4} · f^{1/4}
  float x = r / M;
  float xi = rin / M;
  float f = max(1.0 - sqrt(xi / max(x, xi + 0.01)), 0.0);
  float t = 12.0 * pow(max(x, 1.0), -0.75) * pow(f + 1e-4, 0.25);
  return t; // scaled Kelvin/1000
}

// Keplerian orbital angular velocity Ω = √(M / r³)  (Schwarzschild, from infinity)
float keplerOmega(float r, float M) {
  return sqrt(M / max(r * r * r, 1e-6));
}

vec3 sampleDisk(float r, float phi, float M, float rs, float rin, float rout, float time) {
  if (r < rin || r > rout) return vec3(0.0);

  float omega = keplerOmega(r, M);
  float alpha = alphaOf(r, rs);

  // Orbital 3-speed as seen by a static observer: v = Ω r / √α
  float v = clamp(omega * r / max(sqrt(alpha), 1e-4), 0.0, 0.95);
  // Line-of-sight sense: approaching side (φ ~ +π/2 from +x camera) blueshifts.
  // Photon direction at disk hit is approximately −radial in the disk plane for
  // the primary image; use azimuthal Doppler with sin factor from disk normal.
  float los = sin(phi); // +1 approaching when viewed from +x
  float beta = v;
  float gamma = 1.0 / sqrt(max(1.0 - beta * beta, 1e-4));
  float doppler = 1.0 / (gamma * (1.0 - beta * los));

  // Gravitational redshift: √α
  float g = sqrt(alpha) * doppler;
  g = clamp(g, 0.15, 3.5);

  float T = diskTemperature(r, M, rin) * g;
  vec3 color = blackbody(T);

  // Turbulent structure
  float arms = 0.55 + 0.45 * sin(3.0 * phi - 2.0 * omega * time * 8.0 + 2.0 * log(r));
  float noise = 0.75 + 0.25 * hash21(vec2(floor(phi * 40.0), floor(r * 4.0)));
  float intensity = arms * noise;

  // Brightness boost near ISCO + relativistic beaming ∝ gⁿ
  float emissivity = pow(max(rin / r, 0.05), 1.6) * pow(g, 3.0);
  float falloff = smoothstep(rout, rout * 0.7, r) * smoothstep(rin * 0.95, rin * 1.15, r);

  return color * intensity * emissivity * falloff * 2.8;
}

vec3 sampleSky(vec3 dir) {
  // Equirectangular starfield + procedural Milky Way band
  float phi = atan(dir.z, dir.x);
  float theta = acos(clamp(dir.y, -1.0, 1.0));
  vec2 uv = vec2(phi / TWO_PI + 0.5, theta / PI);
  vec3 stars = texture2D(uStarfield, uv).rgb;

  float milky = exp(-pow((theta - 1.15) / 0.22, 2.0)) * 0.18;
  vec3 band = vec3(0.35, 0.42, 0.65) * milky;

  // Sparse bright stars
  float twinkle = step(0.997, hash21(floor(uv * vec2(900.0, 450.0))));
  stars += twinkle * vec3(1.0, 0.95, 0.85) * 1.5;

  return stars * 0.85 + band;
}

/* ---------- main ray march ---------- */

vec3 trace(vec3 camPos, vec3 camDir, float M) {
  float rs = 2.0 * M;
  float rin = uDiskInner * M;
  float rout = uDiskOuter * M;

  vec3 sph0 = cartToSph(camPos);
  vec3 usph = cartVelToSph(camPos, camDir, sph0);

  GeoState s;
  s.r = sph0.x;
  s.theta = sph0.y;
  s.phi = sph0.z;
  s.ur = usph.x;
  s.utheta = usph.y;
  s.uphi = usph.z;

  // Normalize so that the affine parameter tracks roughly coordinate length
  float speed = length(camDir);
  if (speed > 1e-8) {
    s.ur /= speed;
    s.utheta /= speed;
    s.uphi /= speed;
  }

  vec3 color = vec3(0.0);
  float transmittance = 1.0;
  float prevY = camPos.y;
  int maxSteps = int(uSteps);

  for (int i = 0; i < 512; i++) {
    if (i >= maxSteps) break;

    // Adaptive step: finer near photon sphere / horizon
    float h = mix(0.04, 0.55, smoothstep(rs * 1.2, 25.0 * M, s.r));
    h *= mix(0.35, 1.0, smoothstep(2.5 * M, 6.0 * M, abs(s.r - 3.0 * M)));

    GeoState next = rk4Step(s, h, rs, M);

    // Captured by event horizon
    if (next.r <= rs * 1.02) {
      color += transmittance * vec3(0.0);
      transmittance = 0.0;
      break;
    }

    // Escaped to infinity
    if (next.r > uMaxDistance) {
      vec3 escapeDir = normalize(sphToCart(1.0, next.theta, next.phi));
      // Use velocity direction for better sky sampling
      vec3 p = sphToCart(next.r, next.theta, next.phi);
      vec3 sph = vec3(next.r, next.theta, next.phi);
      vec3 vel = sphToCart(1.0, next.theta, next.phi); // fallback
      // Reconstruct Cartesian velocity from spherical
      float st = sin(next.theta);
      float ct = cos(next.theta);
      float sp = sin(next.phi);
      float cp = cos(next.phi);
      vec3 er = vec3(st * cp, ct, st * sp);
      vec3 et = vec3(ct * cp, -st, ct * sp);
      vec3 ep = vec3(-sp, 0.0, cp);
      vel = normalize(next.ur * er + next.r * next.utheta * et + next.r * st * next.uphi * ep);
      color += transmittance * sampleSky(vel);
      transmittance = 0.0;
      break;
    }

    // Disk intersection: equatorial plane y = 0 (θ = π/2)
    vec3 p0 = sphToCart(s.r, s.theta, s.phi);
    vec3 p1 = sphToCart(next.r, next.theta, next.phi);
    float y0 = p0.y;
    float y1 = p1.y;

    if (y0 * y1 < 0.0) {
      float tHit = y0 / (y0 - y1 + 1e-8);
      vec3 hit = mix(p0, p1, tHit);
      float rHit = length(vec3(hit.x, 0.0, hit.z));
      float phiHit = atan(hit.z, hit.x);

      if (rHit >= rin && rHit <= rout) {
        vec3 diskCol = sampleDisk(rHit, phiHit, M, rs, rin, rout, uTime);
        // Higher-order images (rays looping) accumulate with partial absorption
        float opacity = 0.92;
        color += transmittance * opacity * diskCol;
        transmittance *= (1.0 - opacity);
        if (transmittance < 0.02) break;
      }
    }

    // Soft glow near photon sphere (unstable circular photon orbit)
    float photonGlow = exp(-abs(next.r - 3.0 * M) * 3.0) * 0.012;
    color += transmittance * photonGlow * vec3(1.0, 0.75, 0.45);

    s = next;
    // Keep theta in (0, π)
    s.theta = clamp(s.theta, 1e-3, PI - 1e-3);
  }

  if (transmittance > 0.02) {
    vec3 p = sphToCart(s.r, s.theta, s.phi);
    float st = sin(s.theta);
    float ct = cos(s.theta);
    float sp = sin(s.phi);
    float cp = cos(s.phi);
    vec3 er = vec3(st * cp, ct, st * sp);
    vec3 et = vec3(ct * cp, -st, ct * sp);
    vec3 ep = vec3(-sp, 0.0, cp);
    vec3 vel = normalize(s.ur * er + s.r * s.utheta * et + s.r * st * s.uphi * ep);
    color += transmittance * sampleSky(vel);
  }

  return color;
}

void main() {
  vec2 uv = (gl_FragCoord.xy / uResolution) * 2.0 - 1.0;
  uv.x *= uResolution.x / uResolution.y;

  float tanHalf = tan(uFov * 0.5);
  vec3 rd = normalize(uCameraMatrix * vec3(uv.x * tanHalf, uv.y * tanHalf, -1.0));
  vec3 ro = uCameraPos;

  float M = uMass;
  vec3 col = trace(ro, rd, M);

  // Subtle vignette
  float vig = smoothstep(1.6, 0.3, length(uv * vec2(0.7, 1.0)));
  col *= mix(0.82, 1.0, vig);

  col = tonemapACES(col * 1.15);
  col = pow(col, vec3(1.0 / 2.2));

  gl_FragColor = vec4(col, 1.0);
}
