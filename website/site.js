'use strict';
const layouts = {
  small: {src: 'assets/small.png', alt: 'codeusagemonit 小尺寸原生界面，演示数据', description: '一个大数字，一条额度条。桌面一角就够用。'},
  medium: {src: 'assets/medium.png', alt: 'codeusagemonit 中尺寸原生双栏界面，演示数据', description: '左侧看剩余，右侧看窗口。横向双栏，把常用信息放在一起。'},
  full: {src: 'assets/overview.png', alt: 'codeusagemonit 完整概览原生界面，演示数据', description: '各平台集中展示，费用、Token 和请求记录一览无余。'}
};
const tabs = [...document.querySelectorAll('[data-layout]')];
function selectLayout(tab) {
  const key = tab.dataset.layout, data = layouts[key];
  if (!data) return;
  tabs.forEach(item => {const selected = item === tab; item.setAttribute('aria-selected', String(selected)); item.tabIndex = selected ? 0 : -1;});
  const panel = document.getElementById('layout-panel'); panel.dataset.layout = key; panel.setAttribute('aria-labelledby', tab.id);
  const img = document.getElementById('layout-image'); img.src = data.src; img.alt = data.alt;
  document.getElementById('layout-description').textContent = data.description;
}
tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => selectLayout(tab));
  tab.addEventListener('keydown', event => {
    let target;
    if (event.key === 'ArrowRight') target = (index + 1) % tabs.length;
    else if (event.key === 'ArrowLeft') target = (index + tabs.length - 1) % tabs.length;
    else if (event.key === 'Home') target = 0;
    else if (event.key === 'End') target = tabs.length - 1;
    if (target === undefined) return;
    event.preventDefault(); selectLayout(tabs[target]); tabs[target].focus();
  });
});
document.querySelectorAll('[data-copy]').forEach(button => button.addEventListener('click', async () => {
  const status = document.querySelector('.copy-status');
  try {await navigator.clipboard.writeText(button.dataset.copy); status.textContent = '命令已复制';}
  catch {status.textContent = '请复制：' + button.dataset.copy;}
}));
