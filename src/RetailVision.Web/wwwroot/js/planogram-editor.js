(() => {
    "use strict";
    const data = JSON.parse(document.getElementById("editor-data").value);
    const state = data.document;
    const products = new Map(data.products.map(p => [p.sku, p]));
    const root = document.getElementById("plan-editor");
    const shelves = document.getElementById("shelves");
    const target = document.getElementById("target-shelf");
    const status = document.getElementById("editor-status");
    const save = document.getElementById("save-plan");
    let dirty = false, saving = false, drag = null;
    const el = (tag, text, cls) => {
        const node = document.createElement(tag);
        if (text !== undefined) node.textContent = text;
        if (cls) node.className = cls;
        return node;
    };
    function message(text, error = false) { status.textContent = text; status.classList.toggle("error", error); }
    function changed() { dirty = true; message("Есть несохранённые изменения"); count(); }
    function count() {
        const total = state.shelves.reduce((n, s) => n + s.slots.reduce((m, x) => m + x.facings, 0), 0);
        document.getElementById("editor-count").textContent = state.shelves.length + " полок · " + total + " / 500 фейсингов";
    }
    function normalize() {
        state.shelves.forEach((s, i) => { s.number = i + 1; s.slots.forEach((x, j) => x.position = j + 1); });
    }
    function button(text, label, callback) {
        const b = el("button", text, "icon-button"); b.type = "button"; b.title = label; b.setAttribute("aria-label", label);
        b.addEventListener("click", callback); return b;
    }
    function move(sourceShelf, sourceIndex, destinationShelf, destinationIndex) {
        if (saving) return;
        const from = state.shelves[sourceShelf].slots, to = state.shelves[destinationShelf].slots;
        if (to !== from && to.length >= 100) { message("На одной полке допускается до 100 позиций.", true); return; }
        const [item] = from.splice(sourceIndex, 1);
        if (sourceShelf === destinationShelf && sourceIndex < destinationIndex) destinationIndex--;
        to.splice(destinationIndex, 0, item); normalize(); changed(); render();
    }
    function add(sku, shelfIndex, index) {
        if (saving || !products.get(sku)?.isActive) return;
        const slots = state.shelves[shelfIndex]?.slots;
        if (!slots || slots.length >= 100) { message("На одной полке допускается до 100 позиций.", true); return; }
        slots.splice(index ?? slots.length, 0, { position: 0, sku, facings: 1 });
        normalize(); changed(); render();
    }
    function acceptDrop(event, shelfIndex, position) {
        if (!drag || saving) return;
        event.preventDefault(); event.stopPropagation();
        if (drag.sku) add(drag.sku, shelfIndex, position);
        else move(drag.shelf, drag.index, shelfIndex, position);
        drag = null;
    }
    function renderPalette() {
        const palette = document.getElementById("palette"); palette.replaceChildren();
        const q = document.getElementById("palette-search").value.trim().toLocaleLowerCase("ru");
        for (const product of data.products.filter(p => p.isActive && (p.name + " " + p.sku).toLocaleLowerCase("ru").includes(q))) {
            const row = el("div", undefined, "palette-product"); row.draggable = true;
            const info = el("div"); info.append(el("strong", product.name), el("small", product.sku)); row.append(info);
            row.append(button("+", "Добавить " + product.name, () => add(product.sku, Number(target.value))));
            row.addEventListener("dragstart", e => { drag = { sku: product.sku }; e.dataTransfer.setData("text/plain", product.sku); e.dataTransfer.effectAllowed = "copy"; });
            row.addEventListener("dragend", () => { drag = null; });
            palette.append(row);
        }
        if (!palette.children.length) palette.append(el("p", "Товары не найдены", "file-note"));
    }
    function render() {
        const previous = Math.min(Number(target.value) || 0, state.shelves.length - 1);
        target.replaceChildren();
        state.shelves.forEach((s, i) => { const option = el("option", "Полка " + s.number); option.value = i; target.append(option); });
        target.value = previous;
        shelves.replaceChildren();
        state.shelves.forEach((shelf, shelfIndex) => {
            const section = el("section", undefined, "editor-shelf");
            const header = el("header"); header.append(el("h3", "Полка " + shelf.number));
            const remove = button("Удалить полку", "Удалить пустую полку " + shelf.number, () => {
                if (state.shelves.length <= 1 || shelf.slots.length) return;
                state.shelves.splice(shelfIndex, 1); normalize(); changed(); render();
            });
            remove.disabled = state.shelves.length === 1 || shelf.slots.length > 0; header.append(remove);
            section.append(header);
            const zone = el("div", undefined, "drop-zone");
            zone.addEventListener("dragover", e => { if (drag) { e.preventDefault(); zone.classList.add("drag-over"); } });
            zone.addEventListener("dragleave", () => zone.classList.remove("drag-over"));
            zone.addEventListener("drop", e => acceptDrop(e, shelfIndex, shelf.slots.length));
            if (!shelf.slots.length) zone.append(el("p", "Перетащите товар сюда или нажмите + в каталоге", "empty-shelf"));
            shelf.slots.forEach((slot, index) => {
                const product = products.get(slot.sku);
                const card = el("article", undefined, "slot-card" + (product?.isActive ? "" : " inactive"));
                card.draggable = true; card.dataset.sku = slot.sku;
                card.append(el("small", "ПОЗИЦИЯ " + (index + 1)), el("strong", product?.name ?? slot.sku), el("small", slot.sku));
                if (!product?.isActive) card.append(el("small", " · Неактивен"));
                const inputId = "facings-" + shelfIndex + "-" + index;
                const label = el("label", "Фейсинги"); label.htmlFor = inputId;
                const input = el("input"); input.type = "number"; input.min = "1"; input.max = "100"; input.step = "1"; input.required = true; input.value = slot.facings; input.id = inputId;
                input.addEventListener("input", () => {
                    const value = Number(input.value);
                    if (!Number.isInteger(value) || value < 1 || value > 100) { dirty = true; message("Количество фейсингов: целое число от 1 до 100.", true); return; }
                    slot.facings = value; changed();
                });
                const select = el("select"); select.setAttribute("aria-label", "Переместить " + (product?.name ?? slot.sku) + " на полку");
                state.shelves.forEach((s, i) => { const option = el("option", "Полка " + s.number); option.value = i; select.append(option); });
                select.value = shelfIndex;
                select.addEventListener("change", () => move(shelfIndex, index, Number(select.value), state.shelves[Number(select.value)].slots.length));
                card.append(label, input, el("label", "Размещение"), select);
                const actions = el("div", undefined, "slot-buttons");
                const left = button("←", "Переместить влево", () => move(shelfIndex, index, shelfIndex, index - 1)); left.disabled = index === 0;
                const right = button("→", "Переместить вправо", () => move(shelfIndex, index, shelfIndex, index + 2)); right.disabled = index === shelf.slots.length - 1;
                const removeSlot = button("×", "Убрать " + (product?.name ?? slot.sku), () => { shelf.slots.splice(index, 1); normalize(); changed(); render(); });
                actions.append(left, right, removeSlot); card.append(actions);
                card.addEventListener("dragstart", e => {
                    if (e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement) { e.preventDefault(); return; }
                    drag = { shelf: shelfIndex, index }; e.dataTransfer.setData("text/plain", slot.sku); e.dataTransfer.effectAllowed = "move";
                });
                card.addEventListener("dragend", () => { drag = null; document.querySelectorAll(".drag-over").forEach(z => z.classList.remove("drag-over")); });
                card.addEventListener("drop", e => acceptDrop(e, shelfIndex, index));
                zone.append(card);
            });
            section.append(zone); shelves.append(section);
        });
        document.getElementById("add-shelf").disabled = state.shelves.length >= 10;
        count();
    }
    for (const [id, key] of [["plan-name", "name"], ["plan-store", "store"], ["plan-description", "description"]]) {
        document.getElementById(id).addEventListener("input", e => { state[key] = e.target.value; changed(); });
    }
    document.getElementById("palette-search").addEventListener("input", renderPalette);
    document.getElementById("add-shelf").addEventListener("click", () => {
        if (state.shelves.length >= 10 || saving) return;
        state.shelves.push({ number: state.shelves.length + 1, slots: [] }); changed(); render();
    });
    save.addEventListener("click", async () => {
        if (saving) return;
        if (!state.name.trim()) { message("Укажите название планограммы.", true); document.getElementById("plan-name").focus(); return; }
        const invalid = [...root.querySelectorAll("input")].find(input => !input.checkValidity());
        if (invalid) { invalid.reportValidity(); message("Проверьте выделенное поле.", true); return; }
        saving = true; save.disabled = true; root.inert = true; message("Сохраняем…");
        try {
            const response = await fetch("/planograms/save", {
                method: "POST", credentials: "same-origin",
                headers: { "Content-Type": "application/json", "RequestVerificationToken": root.querySelector('input[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ id: data.id, version: data.version, document: state })
            });
            if (response.redirected || !response.headers.get("content-type")?.includes("application/json"))
                throw new Error("Не удалось сохранить. Возможно, сессия завершилась. Войдите в другом окне и повторите сохранение.");
            const result = await response.json();
            if (!response.ok) throw new Error(result.errors?.join("\n") ?? "Не удалось сохранить планограмму.");
            data.id = result.id; data.version = result.version; dirty = false;
            history.replaceState(null, "", result.url);
            message("Сохранено · версия " + result.version);
        } catch (error) { message(error instanceof Error ? error.message : "Ошибка соединения. Изменения остаются в редакторе.", true); }
        finally { saving = false; save.disabled = false; root.inert = false; }
    });
    window.addEventListener("beforeunload", e => { if (dirty) { e.preventDefault(); e.returnValue = ""; } });
    renderPalette(); render();
})();
