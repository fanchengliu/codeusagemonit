# Schwarzschild Black Hole Simulation

基于 **Schwarzschild 度规** 的广义相对论零测地线（null geodesic）光线追踪模拟。浏览器打开即可观看，支持 OrbitControls 旋转 / 缩放 / 平移。

## 物理模型

| 量 | 公式 / 取值 |
|---|---|
| 度规 | \(ds^2 = -\alpha\,dt^2 + \alpha^{-1}dr^2 + r^2 d\Omega^2\)，\(\alpha = 1 - r_s/r\) |
| 事件视界 | \(r_s = 2M\) |
| 光子球 | \(r_{ph} = 3M\) |
| ISCO | \(r_{isco} = 6M\) |
| 光线 | 测地线方程 \(\frac{d^2 x^\mu}{d\lambda^2} + \Gamma^\mu_{\alpha\beta} u^\alpha u^\beta = 0\)，RK4 积分 |
| 吸积盘 | Novikov–Thorne 温度剖面 + Kepler 轨道 + 引力红移 \(\sqrt{\alpha}\) + 相对论多普勒 |

实现位于 `src/shaders/blackhole.frag.glsl`。

## 项目结构

```
black-hole-sim/
├── index.html
├── package.json
├── vite.config.js
├── README.md
└── src/
    ├── main.js                 # Three.js + OrbitControls
    ├── styles.css
    └── shaders/
        ├── blackhole.vert.glsl
        └── blackhole.frag.glsl # GR 光线追踪核心
```

## 安装与运行

需要 Node.js 18+。

```bash
cd black-hole-sim
npm install
npm run dev
```

浏览器访问终端提示的本地地址（默认 `http://localhost:5173`）。

生产构建：

```bash
npm run build
npm run preview
```

## 操作

- **左键拖拽**：旋转视角（OrbitControls）
- **滚轮**：缩放
- **右键拖拽**：平移

## 技术栈

- [Three.js](https://threejs.org/) — WebGL 渲染与 OrbitControls
- Vite — 开发服务器与构建
- GLSL — GPU 上的 Schwarzschild 测地线积分
