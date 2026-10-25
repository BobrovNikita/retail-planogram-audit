"use strict";
(async () => {
    const root = document.getElementById("review-editor"); if (!root) return;
    const svg = document.getElementById("review-canvas"), boxes = document.getElementById("review-boxes"), list = document.getElementById("review-objects");
    const status = document.getElementById("review-status"), form = document.getElementById("review-form"), add = document.getElementById("add-detection");
    const save = document.getElementById("save-review"), restore = document.getElementById("restore-review"), reason = document.getElementById("review-reason");
    let rows = [], snapshot, selected = null, dirty = false, saving = false, revision;
    const ns = "http://www.w3.org/2000/svg";
    function element(tag, text, cls) { const e = document.createElement(tag); if (text) e.textContent = text; if (cls) e.className = cls; return e; }
    function rectangle(d, group, cls) { const r = document.createElementNS(ns, "rect"); for (const [k,v] of Object.entries({x:d.x*snapshot.width,y:d.y*snapshot.height,width:d.width*snapshot.width,height:d.height*snapshot.height})) r.setAttribute(k,v); r.setAttribute("class",cls); group.append(r); return r; }
    function draw() { boxes.replaceChildren(); for (const d of rows) { const r = rectangle(d, boxes, d.id === selected ? "review-box selected" : "review-box"); const t = document.createElementNS(ns,"title"); t.textContent=d.sku||"Неизвестный SKU"; r.append(t); } }
    function mark() { dirty=true; status.textContent="Есть несохранённые изменения."; draw(); }
    function render() {
        list.replaceChildren();
        for (const [i,d] of rows.entries()) {
            const card = element("fieldset",null,"review-object"); card.dataset.id=d.id;
            const legend=element("legend"); const choose=element("button",`${i+1}. ${d.sku||"Неизвестный объект"}`,"text-button"); choose.type="button"; choose.setAttribute("aria-pressed",String(selected===d.id));
            choose.addEventListener("click",()=>{selected=d.id;render();});legend.append(choose);card.append(legend);
            const label=element("label","SKU"); const select=element("select"); const unknown=element("option","Неизвестен");unknown.value="";select.append(unknown);
            for(const [sku,name] of Object.entries(snapshot.productNames)){const opt=element("option",`${sku} · ${name}`);opt.value=sku;select.append(opt);}select.value=d.sku||"";
            select.addEventListener("change",()=>{d.sku=select.value||null;d.confirmed=!!d.sku;choose.textContent=`${i+1}. ${d.sku||"Неизвестный объект"}`;confirm.checked=d.confirmed;mark();});label.append(select);card.append(label);
            const coords=element("div",null,"review-coordinates");
            for(const [key,title] of [["x","Слева, %"],["y","Сверху, %"],["width","Ширина, %"],["height","Высота, %"]]){
                const l=element("label",title), input=element("input");input.type="number";input.min=key==="width"||key==="height"?"0.001":"0";input.max="100";input.step="any";input.required=true;input.value=Number((d[key]*100).toFixed(5));
                input.addEventListener("input",()=>{d[key]=input.value===""?NaN:Number(input.value)/100;d.confirmed=!!d.sku;confirm.checked=d.confirmed;mark();});l.append(input);coords.append(l);
            }card.append(coords);
            const confirmLabel=element("label",null,"review-confirm"),confirm=element("input");confirm.type="checkbox";confirm.checked=d.confirmed;confirm.addEventListener("change",()=>{d.confirmed=confirm.checked;mark();});confirmLabel.append(confirm,document.createTextNode("Подтверждаю SKU и рамку вручную"));card.append(confirmLabel);
            const raw=snapshot.originalDetection.products.find(p=>p.id===d.id);card.append(element("p",raw?`Исходная уверенность: ${(raw.confidence*100).toFixed(1)}%. Значение модели сохраняется.`:"Добавлен вручную; оценки модели нет.","file-note"));
            const remove=element("button","Удалить объект","text-button");remove.type="button";remove.addEventListener("click",()=>{rows=rows.filter(x=>x.id!==d.id);if(selected===d.id)selected=rows[0]?.id??null;mark();render();});card.append(remove);list.append(card);
        }
        if(!rows.length)list.append(element("p","Объектов нет. Можно добавить пропущенный объект или сохранить пустой результат.","file-note"));draw();
    }
    function point(event) { const p = new DOMPoint(event.clientX,event.clientY).matrixTransform(svg.getScreenCTM().inverse()); return {x:Math.max(0,Math.min(1,p.x/snapshot.width)),y:Math.max(0,Math.min(1,p.y/snapshot.height))}; }
    let start=null;
    svg.addEventListener("pointerdown",e=>{if(saving||!selected||e.button!==0)return;start=point(e);svg.setPointerCapture(e.pointerId);e.preventDefault();});
    svg.addEventListener("pointerup",e=>{if(!start)return;const end=point(e),d=rows.find(x=>x.id===selected);const x=Math.min(start.x,end.x),y=Math.min(start.y,end.y),width=Math.abs(end.x-start.x),height=Math.abs(end.y-start.y);start=null;if(width<.002||height<.002)return;Object.assign(d,{x,y,width,height,confirmed:!!d.sku});mark();render();});
    svg.addEventListener("pointercancel",()=>{start=null;});
    add.addEventListener("click",()=>{if(rows.length>=500){status.textContent="Максимум 500 объектов.";return;}const d={id:"manual-"+crypto.randomUUID().replaceAll("-",""),sku:null,x:.1,y:.1,width:.1,height:.2,confirmed:false};rows.push(d);selected=d.id;mark();render();list.lastElementChild.scrollIntoView({block:"nearest"});});
    async function submit(isRestore) {
        if(saving||!form.reportValidity())return;
        if(!isRestore && (!Array.from(list.querySelectorAll("input,select")).every(i=>i.reportValidity()) || rows.some(d=>![d.x,d.y,d.width,d.height].every(Number.isFinite))))return;
        saving=true; root.querySelectorAll("input,select,textarea,button").forEach(e=>e.disabled=true);status.textContent="Сохраняем и пересчитываем…";
        try {const response=await fetch(`/history/correct/${root.dataset.id}`,{method:"POST",headers:{"Content-Type":"application/json","RequestVerificationToken":form.querySelector('[name="__RequestVerificationToken"]').value},body:JSON.stringify({revision,reason:reason.value,detections:rows,restore:isRestore})});
            const result=await response.json().catch(()=>({error:"Не удалось сохранить. Проверьте соединение и обновите страницу."}));if(!response.ok)throw new Error(result.error);dirty=false;location.assign(result.url);
        }catch(error){status.textContent=error.message;saving=false;root.querySelectorAll("input,select,textarea,button").forEach(e=>e.disabled=false);}
    }
    form.addEventListener("submit",e=>{e.preventDefault();submit(false);});restore.addEventListener("click",()=>submit(true));
    window.addEventListener("beforeunload",e=>{if(dirty){e.preventDefault();e.returnValue="";}});
    try {const response=await fetch(`/history/reviewdata/${root.dataset.id}`);if(!response.ok)throw new Error("Не удалось загрузить проверку.");const data=await response.json();snapshot=data.snapshot;revision=data.revision;
        rows=data.current.map(r=>({id:r.detection.id,sku:r.detection.sku,x:r.detection.x,y:r.detection.y,width:r.detection.width,height:r.detection.height,confirmed:r.confirmedByHuman}));selected=rows[0]?.id??null;
        if(root.dataset.mode==="DEMO")for(const d of snapshot.originalDetection.products)rectangle(d,document.getElementById("review-background"),"review-fixture");
        render();add.disabled=save.disabled=restore.disabled=false;status.textContent="Выберите объект для исправления.";
    }catch(error){status.textContent=error.message;}
})();
