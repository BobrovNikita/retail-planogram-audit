(() => {
    'use strict';
    const root = document.getElementById('calibration');
    if (!root) return;
    const form = document.getElementById('calibration-form');
    const svg = document.getElementById('photo-canvas');
    const overlays = document.getElementById('shelf-overlays');
    const status = document.getElementById('calibration-status');
    const analyze = document.getElementById('analyze-photo');
    const revision = analyze.querySelector('[name=revision]');
    const fields = [...form.querySelectorAll('.shelf-fields')];
    const width = Number(root.dataset.width), height = Number(root.dataset.height);
    let dirty = false, saving = false, start = null, dragField = null;
    const active = () => fields.find(f => f.querySelector('input[type=radio]').checked);
    const bands = () => fields.map(f => {
        const band = { shelf: Number(f.dataset.shelf) };
        f.querySelectorAll('[data-coordinate]').forEach(input => band[input.dataset.coordinate] = input.valueAsNumber / 100);
        return band;
    });
    function draw() {
        overlays.replaceChildren();
        bands().forEach(b => {
            if (![b.left,b.right,b.top,b.bottom].every(Number.isFinite) || b.right <= b.left || b.bottom <= b.top) return;
            const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
            Object.entries({x:b.left*width,y:b.top*height,width:(b.right-b.left)*width,height:(b.bottom-b.top)*height,'data-shelf':b.shelf}).forEach(([k,v])=>rect.setAttribute(k,String(v)));
            rect.classList.toggle('active-band', Number(active()?.dataset.shelf) === b.shelf);
            overlays.append(rect);
        });
    }
    function changed() {
        dirty = true;
        analyze.querySelector('button').disabled = true;
        status.textContent = 'Есть несохранённые изменения. Сохраните границы перед анализом.';
        const report = document.getElementById('photo-report'); if (report) report.hidden = true;
        const detections = document.getElementById('photo-detections'); if (detections) detections.remove();
        draw();
    }
    form.addEventListener('input', e => { if(e.target.type === 'radio') draw(); else changed(); });
    const point = e => {
        const r = svg.getBoundingClientRect();
        return {x:Math.min(1,Math.max(0,(e.clientX-r.left)/r.width)), y:Math.min(1,Math.max(0,(e.clientY-r.top)/r.height))};
    };
    svg.addEventListener('pointerdown', e => {
        if (saving || e.button !== 0) return;
        start = point(e); dragField = active(); if(!dragField) return;
        svg.setPointerCapture(e.pointerId); e.preventDefault();
    });
    function drag(e) {
        if(!start || !dragField) return;
        const end = point(e);
        const values = {left:Math.min(start.x,end.x),right:Math.max(start.x,end.x),top:Math.min(start.y,end.y),bottom:Math.max(start.y,end.y)};
        dragField.querySelectorAll('[data-coordinate]').forEach(input=>input.value=(values[input.dataset.coordinate]*100).toFixed(1));
        changed();
    }
    svg.addEventListener('pointermove', drag);
    svg.addEventListener('pointerup', e => { drag(e); start=null; dragField=null; if(svg.hasPointerCapture(e.pointerId)) svg.releasePointerCapture(e.pointerId); });
    svg.addEventListener('pointercancel', ()=> {start=null;dragField=null;});
    form.addEventListener('submit', async e => {
        e.preventDefault(); if(saving || !form.reportValidity()) return;
        const payload = {revision:Number(revision.value),shelves:bands(),confidenceThreshold:document.getElementById('confidence').valueAsNumber/100};
        saving=true; form.querySelectorAll('input,button').forEach(x=>x.disabled=true); status.textContent='Сохраняем границы…';
        try {
            const response=await fetch('/photoaudit/calibrate/'+root.dataset.id,{method:'POST',headers:{'Content-Type':'application/json','RequestVerificationToken':form.querySelector('[name=__RequestVerificationToken]').value},body:JSON.stringify(payload)});
            const result=await response.json().catch(()=>({error:response.status===401 || response.redirected?'Войдите заново и обновите страницу.':'Не удалось сохранить границы.'}));
            if(!response.ok || !result.revision) throw new Error(result.error || 'Не удалось сохранить границы.');
            revision.value=result.revision; dirty=false; status.textContent='Калибровка сохранена.'; analyze.querySelector('button').disabled=root.dataset.ready!=='true';
        } catch(error) {status.textContent=error.message || 'Сетевая ошибка. Повторите сохранение.';}
        finally {saving=false;form.querySelectorAll('input,button').forEach(x=>x.disabled=false);}
    });
    analyze.addEventListener('submit',e=> {if(dirty || saving){e.preventDefault();return;} analyze.querySelector('button').disabled=true;analyze.querySelector('button').textContent='Распознаём товары…';});
    window.addEventListener('beforeunload',e=>{if(dirty){e.preventDefault();e.returnValue='';}});
    draw();
})();
