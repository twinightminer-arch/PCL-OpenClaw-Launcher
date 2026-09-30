/* Local Wallpaper Engine bridge, OCL 0.6.0 / plugin 0.3.0. */
(() => {
  "use strict";
  if (window.OpenClawWallpaper) return;
  const base = new URL(".", document.currentScript.src);
  let signature = "", config = null, loading = false;
  const style = document.createElement("style");
  style.textContent = `
    #oc-wallpaper-layer,#oc-wallpaper-scrim{position:fixed;inset:0;pointer-events:none;z-index:0}
    #oc-wallpaper-layer{width:100%;height:100%;background-position:center;background-repeat:no-repeat}
    html.oc-wallpaper body{background:transparent!important}
    html.oc-wallpaper openclaw-app{position:relative;z-index:1;--bg:transparent;--bg-accent:transparent}
    html.oc-wallpaper .shell{background:color-mix(in srgb,var(--panel,#0e1015) var(--oc-app-alpha,45%),transparent)!important}
    html.oc-wallpaper .content,html.oc-wallpaper .chat,html.oc-wallpaper .chat-main,html.oc-wallpaper openclaw-app-shell{background:transparent!important}
    html.oc-wallpaper .shell-nav,html.oc-wallpaper .chat-compose,html.oc-wallpaper .topbar{background:color-mix(in srgb,var(--panel,#0e1015) 78%,transparent)!important;backdrop-filter:blur(10px)}
    html.oc-font openclaw-app{font-size:var(--oc-font-size,inherit);font-family:var(--oc-font-family,inherit);font-weight:var(--oc-font-weight,inherit)}
    html.oc-font openclaw-app :is(span,p,div,label,code,pre,li,td,th,h1,h2,h3,h4,h5,h6,b,strong,em,small,a,mark,button,input,textarea,select){color:var(--oc-font-color,#f2f3f5)!important}
    html.oc-font{caret-color:var(--oc-caret,#ffffff)}
    html.oc-font ::selection{background:var(--oc-caret,#4f8cff);color:#fff}
    #oc-wallpaper-launch{position:fixed;right:18px;bottom:16px;z-index:1000;border:1px solid #d9363e;border-radius:20px;padding:8px 16px;background:#b92535;color:white;cursor:pointer;font:14px system-ui}
    #oc-wallpaper-panel{position:fixed;right:18px;bottom:64px;z-index:1001;width:min(440px,calc(100vw - 36px));max-height:78vh;overflow:auto;border:1px solid #80414b;border-radius:16px;padding:18px;box-sizing:border-box;background:#171b24;color:#f2f3f5;box-shadow:0 12px 50px #0007;font:14px system-ui}
    #oc-wallpaper-panel[hidden]{display:none}
    #oc-wallpaper-panel h2{font-size:18px;margin:0 0 8px}
    #oc-wallpaper-panel p{font-size:12px;color:#bbc1ce;line-height:1.6}
    #oc-wallpaper-panel button,#oc-wallpaper-panel input{box-sizing:border-box;font:inherit;border:1px solid #525b70;border-radius:8px;padding:8px;background:#272e3c;color:#fff;margin:3px 3px 3px 0}
    #oc-wallpaper-panel button{cursor:pointer}#oc-wallpaper-panel button:disabled{opacity:.5;cursor:wait}
    #oc-wallpaper-panel .oc-list{display:grid;gap:6px;max-height:30vh;overflow:auto;margin-top:10px}
    #oc-wallpaper-panel .oc-list button{text-align:left;margin:0;line-height:1.5}
    #oc-wallpaper-panel .oc-list button:hover{border-color:#f36575;background:#3b2934}
    #oc-wallpaper-panel input[type=search]{width:100%;margin-top:10px}
    #oc-wallpaper-panel label{display:block;margin:10px 0 4px}
    #oc-wallpaper-panel .oc-inline{display:flex;align-items:center;gap:8px;margin:10px 0 2px}
    #oc-wallpaper-panel .oc-inline span{font-size:13px}
    #oc-wallpaper-panel .oc-hr{border:0;border-top:1px solid #333a48;margin:14px 0 6px}
    #oc-wallpaper-panel input[type=color]{padding:2px;width:52px;height:34px}
  `;
  document.head.append(style);
  function removeLayer() {
    document.getElementById("oc-wallpaper-layer")?.remove();
    document.getElementById("oc-wallpaper-scrim")?.remove();
    document.documentElement.classList.remove("oc-wallpaper");
  }
  function applyTypography(next) {
    const root = document.documentElement;
    if (next && next.fontCustom) {
      const color = /^#[0-9a-f]{6}$/i.test(next.fontColor) ? next.fontColor : "#f2f3f5";
      root.style.setProperty("--oc-font-color", color);
      const size = Number(next.fontSize);
      if (Number.isFinite(size) && size >= 10 && size <= 28) root.style.setProperty("--oc-font-size", size + "px");
      else root.style.removeProperty("--oc-font-size");
      const weight = Number(next.fontWeight);
      if (Number.isFinite(weight) && weight >= 100 && weight <= 900) root.style.setProperty("--oc-font-weight", String(Math.round(weight / 100) * 100));
      else root.style.removeProperty("--oc-font-weight");
      root.style.setProperty("--oc-font-family", next.fontFamily && String(next.fontFamily).trim() ? String(next.fontFamily).trim() : "inherit");
      root.style.setProperty("--oc-caret", /^#[0-9a-f]{6}$/i.test(next.caretColor) ? next.caretColor : "#ffffff");
      root.classList.add("oc-font");
    } else {
      for (const v of ["--oc-font-color", "--oc-font-size", "--oc-font-weight", "--oc-font-family", "--oc-caret"]) root.style.removeProperty(v);
      root.classList.remove("oc-font");
    }
  }
  function apply(next) {
    config = next;
    applyTypography(next);
    if (!next?.enabled || !next.image) { removeLayer(); return; }
    const url = new URL(next.image, base);
    if (url.origin !== base.origin || !url.pathname.startsWith(base.pathname)) return;
    url.searchParams.set("v", next.updatedAt || "0");
    const video = next.mediaType === "video";
    const layer = document.createElement(video ? "video" : "div");
    layer.id = "oc-wallpaper-layer";
    layer.setAttribute("aria-hidden", "true");
    const fit = ["cover","contain","auto"].includes(next.fit) ? next.fit : "cover";
    if (video) {
      layer.muted = true; layer.loop = true; layer.playsInline = true;
      layer.autoplay = !matchMedia("(prefers-reduced-motion: reduce)").matches;
      layer.src = url.href; layer.style.objectFit = fit === "auto" ? "contain" : fit;
    } else { layer.style.backgroundImage = `url(${JSON.stringify(url.href)})`; layer.style.backgroundSize = fit; }
    layer.style.opacity = Math.min(1,Math.max(0,Number(next.opacity ?? .65)));
    layer.style.filter = `blur(${Math.min(40,Math.max(0,Number(next.blur || 0)))}px)`;
    const scrim = document.createElement("div");scrim.id="oc-wallpaper-scrim";
    scrim.style.background = /^#[a-f0-9]{6}$/i.test(next.scrimColor) ? next.scrimColor : "#0b0e13";
    scrim.style.opacity = Math.min(1,Math.max(0,Number(next.scrim ?? .3)));
    removeLayer();document.documentElement.style.setProperty("--oc-app-alpha", `${next.translucentApp === false ? 100 : Math.round(Math.min(1,Math.max(0,Number(next.appAlpha ?? .45)))*100)}%`);document.body.prepend(layer,scrim);document.documentElement.classList.add("oc-wallpaper");
    if (video && layer.autoplay) layer.play().catch(()=>{});
  }
  async function refresh(force = false) {
    if (loading) return; loading=true;
    try {
      const response = await fetch(new URL("wallpaper.json",base),{cache:"no-store",credentials:"same-origin"});
      if (!response.ok) return;
      const next=await response.json(),key=JSON.stringify(next);
      if(force||key!==signature){signature=key;apply(next);}
    } catch { /* Keep the last working background during gateway restarts. */ }
    finally { loading=false; }
  }
  // Verified against the installed Light DOM host. Fail explicitly if an upgrade changes the bridge.
  async function rpc(method, params = {}) {
    const app=document.querySelector("openclaw-app");
    const gateway=app?.context?.gateway;
    if (gateway?.snapshot?.phase !== "connected" || !gateway.snapshot.client) throw new Error("请先连接 OpenClaw 网关，再重试。若刚安装插件，请重启网关并刷新网页。");
    return gateway.snapshot.client.request(method,params);
  }
  function boot() {
    const launch=document.createElement("button");launch.id="oc-wallpaper-launch";launch.textContent="壁纸";launch.setAttribute("aria-expanded","false");
    const panel=document.createElement("section");panel.id="oc-wallpaper-panel";panel.hidden=true;panel.setAttribute("aria-label","Wallpaper Engine 壁纸库");
    const heading=document.createElement("h2");heading.textContent="Wallpaper Engine";panel.append(heading);
    const note=document.createElement("p");note.textContent="图片 / GIF / 视频可用；原生场景采用预览图。可直接导入本地媒体文件。";panel.append(note);
    const message=document.createElement("p");message.setAttribute("role","status");panel.append(message);
    const controls=document.createElement("div");panel.append(controls);
    const search=document.createElement("input");search.type="search";search.placeholder="搜索壁纸名称或 ID";search.setAttribute("aria-label","搜索壁纸");panel.append(search);
    const list=document.createElement("div");list.className="oc-list";panel.append(list);
    let entries=[],busy=false;
    async function run(action) {
      if(busy)return;busy=true;panel.querySelectorAll("button").forEach(b=>b.disabled=true);message.textContent="正在处理…";
      try{await action();}catch(error){message.textContent="操作失败："+error.message;}finally{busy=false;panel.querySelectorAll("button").forEach(b=>b.disabled=false);}
    }
    function button(label,fn,parent=controls){const b=document.createElement("button");b.textContent=label;b.onclick=()=>run(fn);parent.append(b);return b;}
    function render(){list.replaceChildren();const needle=search.value.toLocaleLowerCase();for(const item of entries.filter(x=>(x.title+" "+x.id).toLocaleLowerCase().includes(needle)))button(item.title+" · "+item.type,async()=>{
      const result=await rpc("wallpaper.set",{query:item.id});if(!result?.applied)throw new Error(result?.reason||result?.hint||"未能应用壁纸");
      await refresh(true);message.textContent="已应用："+item.title;
    },list);}
    async function load(){const result=await rpc("wallpaper.list");entries=result.wallpapers||[];render();message.textContent=`已连接 · ${result.total ?? entries.length} 张壁纸`;}
    button("刷新壁纸库",load);
    button("关闭背景",async()=>{await rpc("wallpaper.off");await refresh(true);message.textContent="背景已关闭";});
    button("收起",async()=>{panel.hidden=true;launch.setAttribute("aria-expanded","false");launch.focus();});
    const label=document.createElement("label");label.textContent="背景亮度";const opacity=document.createElement("input");opacity.type="range";opacity.min="0.1";opacity.max="1";opacity.step=".05";opacity.value=".65";label.append(opacity);panel.append(label);
    opacity.onchange=()=>run(async()=>{await rpc("wallpaper.configure",{opacity:Number(opacity.value)});await refresh(true);message.textContent="显示设置已更新";});

    panel.append(Object.assign(document.createElement("hr"),{className:"oc-hr"}));
    const fontRow=document.createElement("label");fontRow.className="oc-inline";const fontCheck=document.createElement("input");fontCheck.type="checkbox";const fontSpan=document.createElement("span");fontSpan.textContent="自定义控制台字体颜色 / 字号";fontRow.append(fontCheck,fontSpan);panel.append(fontRow);
    const colorLabel=document.createElement("label");colorLabel.textContent="字体颜色";const fontColor=document.createElement("input");fontColor.type="color";fontColor.value="#f2f3f5";colorLabel.append(fontColor);panel.append(colorLabel);
    const sizeLabel=document.createElement("label");sizeLabel.textContent="字号（0 = 默认）";const fontSize=document.createElement("input");fontSize.type="range";fontSize.min="0";fontSize.max="24";fontSize.step="1";fontSize.value="0";sizeLabel.append(fontSize);panel.append(sizeLabel);
    fontCheck.onchange=()=>run(async()=>{await rpc("wallpaper.configure",{fontCustom:fontCheck.checked,fontColor:fontColor.value,fontSize:Number(fontSize.value)});await refresh(true);message.textContent=fontCheck.checked?"已启用自定义字体":"已恢复默认字体";});
    fontColor.onchange=()=>run(async()=>{fontCheck.checked=true;await rpc("wallpaper.configure",{fontCustom:true,fontColor:fontColor.value});await refresh(true);message.textContent="字体颜色已更新";});
    fontSize.onchange=()=>run(async()=>{fontCheck.checked=true;await rpc("wallpaper.configure",{fontCustom:true,fontSize:Number(fontSize.value)});await refresh(true);message.textContent="字号已更新";});

    const importLabel=document.createElement("label");importLabel.textContent="导入本地媒体（绝对路径）";const importPath=document.createElement("input");importPath.type="text";importPath.placeholder="例如 D:\\Pictures\\背景.mp4";importPath.style.width="100%";importLabel.append(importPath);panel.append(importLabel);
    button("导入并应用",async()=>{const p=importPath.value.trim();if(!p)throw new Error("请填写本地文件的绝对路径");const r=await rpc("wallpaper.import",{path:p});if(!r?.applied)throw new Error(r?.reason||"导入失败");await refresh(true);message.textContent="已导入："+(r.config?.title||p);});

    search.oninput=render;
    launch.onclick=()=>{panel.hidden=!panel.hidden;launch.setAttribute("aria-expanded",String(!panel.hidden));if(!panel.hidden){opacity.value=String(config?.opacity??.65);fontCheck.checked=!!config?.fontCustom;fontColor.value=config?.fontColor||"#f2f3f5";fontSize.value=String(config?.fontSize||0);search.focus();if(!entries.length)run(load);}};
    document.addEventListener("keydown",e=>{if(e.key==="Escape"&&!panel.hidden){panel.hidden=true;launch.setAttribute("aria-expanded","false");launch.focus();}});
    document.body.append(launch,panel);refresh(true);
    setInterval(()=>{if(!document.hidden)refresh();},4000);
    document.addEventListener("visibilitychange",()=>{const video=document.querySelector("video#oc-wallpaper-layer");if(document.hidden)video?.pause();else{refresh();if(video?.autoplay)video.play().catch(()=>{});}});
  }
  window.OpenClawWallpaper={refresh};
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot,{once:true});else boot();
})();
