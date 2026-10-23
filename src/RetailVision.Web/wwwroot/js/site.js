"use strict";
document.querySelectorAll("[data-loading-form]").forEach(form => {
    form.addEventListener("submit", () => {
        const button = form.querySelector("button[type=submit]");
        button.disabled = true;
        button.textContent = button.dataset.loadingText;
        form.querySelector("[role=status]").hidden = false;
    });
});
document.querySelectorAll("[data-highlight]").forEach(button => {
    button.addEventListener("click", () => {
        document.querySelectorAll(".highlighted").forEach(item => item.classList.remove("highlighted"));
        const target = document.getElementById(button.dataset.highlight);
        if (!target) return;
        target.classList.add("highlighted");
        target.scrollIntoView({ behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth", block: "center" });
    });
});
window.addEventListener("pageshow", event => {
    if (event.persisted) window.location.reload();
});
