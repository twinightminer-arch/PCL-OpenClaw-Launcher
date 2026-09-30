import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, readFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import plugin from '../dist/index.js';

test('wallpaper plugin: real registration, isolated config, assets and authenticated RPC handlers', async () => {
  const root=await mkdtemp(path.join(tmpdir(),'wallpaper-test-'));
  try {
    const libraryRoot=path.join(root,'library'),controlUiRoot=path.join(root,'ui');
    await mkdir(path.join(libraryRoot,'123'),{recursive:true});await mkdir(controlUiRoot);
    await writeFile(path.join(controlUiRoot,'index.html'),'<html><head></head><body></body></html>');
    await writeFile(path.join(libraryRoot,'123','project.json'),JSON.stringify({title:'Fixture GIF',type:'scene',preview:'preview.gif',file:'scene.pkg'}));
    await writeFile(path.join(libraryRoot,'123','preview.gif'),'GIF89a');
    await writeFile(path.join(root,'outside.png'),'outside');
    await mkdir(path.join(libraryRoot,'456'));
    await writeFile(path.join(libraryRoot,'456','project.json'),JSON.stringify({title:'Outside',preview:'../../outside.png'}));
    const tools=new Map(),methods=new Map(),services=[];
    plugin.register({pluginConfig:{libraryRoot,controlUiRoot},registerTool:t=>tools.set(t.name,t),registerGatewayMethod:(name,handler,options)=>methods.set(name,{handler,options}),registerService:s=>services.push(s)});
    assert.equal(tools.size,9);assert.equal(methods.size,6);
    assert.equal(methods.get('wallpaper.set').options.scope,'operator.write');
    assert.equal(methods.get('wallpaper.list').options.scope,'operator.read');
    await services[0].start();await services[0].start();
    const html=await readFile(path.join(controlUiRoot,'index.html'),'utf8');
    assert.equal(html.match(/wallpaper\/wallpaper.js/g).length,1);
    const list=(await tools.get('wallpaper_list').execute('test',{})).details;
    assert.equal(list.total,2);assert.equal(list.libraryRoot,libraryRoot);
    const applied=(await tools.get('wallpaper_ui_set').execute('test',{query:'123'})).details;
    assert.equal(applied.applied,true);assert.equal(applied.config.image,'wp-123.gif');
    assert.equal(await readFile(applied.uiAsset,'utf8'),'GIF89a');
    assert.equal(applied.config.translucentApp,true);
    const escaped=(await tools.get('wallpaper_ui_set').execute('test',{query:'456'})).details;
    assert.equal(escaped.applied,false);
    let response;
    await methods.get('wallpaper.configure').handler({params:{opacity:4},respond:(...value)=>response=value});
    assert.equal(response[0],false);
    await methods.get('wallpaper.set').handler({params:{},respond:(...value)=>response=value});assert.equal(response[0],false);
    await methods.get('wallpaper.off').handler({params:{},respond:(...value)=>response=value});assert.equal(response[0],true);
    assert.equal(JSON.parse(await readFile(path.join(controlUiRoot,'wallpaper','wallpaper.json'),'utf8')).enabled,false);
    await writeFile(path.join(libraryRoot,'123','film.mp4'),'video-fixture');
    await writeFile(path.join(libraryRoot,'123','project.json'),JSON.stringify({title:'Video',type:'video',file:'film.mp4',preview:'preview.gif'}));
    const video=(await tools.get('wallpaper_ui_set').execute('test',{query:'123'})).details;
    assert.equal(video.config.mediaType,'video');assert.equal(video.config.image,'wp-123.mp4');
    assert.equal(await readFile(video.uiAsset,'utf8'),'video-fixture');
    // Direct import of an arbitrary local media file (not from the library).
    await writeFile(path.join(root,'local-bg.png'),'PNG-fixture');
    const imported=(await tools.get('wallpaper_ui_import').execute('test',{path:path.join(root,'local-bg.png')})).details;
    assert.equal(imported.applied,true);assert.equal(imported.kind,'image');
    assert.equal(await readFile(imported.uiAsset,'utf8'),'PNG-fixture');
    assert.equal(imported.config.title,'local-bg.png');assert.equal(imported.config.mediaType,'image');
    await assert.rejects(()=>Promise.resolve(tools.get('wallpaper_ui_import').execute('test',{path:path.join(root,'local-bg.png')+'.txt'})));
    // Console typography via RPC.
    await methods.get('wallpaper.configure').handler({params:{fontCustom:true,fontColor:'#101010',fontSize:15},respond:(...v)=>response=v});
    assert.equal(response[0],true);assert.equal(response[1].config.fontColor,'#101010');assert.equal(response[1].config.fontSize,15);assert.equal(response[1].config.fontCustom,true);
    await methods.get('wallpaper.configure').handler({params:{fontColor:'not-a-color'},respond:(...v)=>response=v});assert.equal(response[0],false);
    await methods.get('wallpaper.import').handler({params:{},respond:(...v)=>response=v});assert.equal(response[0],false);
  } finally {await rm(root,{recursive:true,force:true});}
});
