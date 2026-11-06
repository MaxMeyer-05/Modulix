(() => {
    const displayDurationMs = 6000;
    const region = document.getElementById("toast-region");
    const list = region?.querySelector("[data-toast-list]");
    const source = region?.querySelector("template[data-toast-source]");

    if (!list || !source) {
        return;
    }

    const prefersReducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    const dismiss = toast => {
        if (toast.dataset.leaving === "true") {
            return;
        }

        toast.dataset.leaving = "true";
        window.clearTimeout(Number(toast.dataset.timer));

        if (toast.contains(document.activeElement)) {
            document.activeElement.blur();
        }

        if (prefersReducedMotion()) {
            toast.remove();
            return;
        }

        toast.classList.add("is-leaving");
        toast.addEventListener("transitionend", () => toast.remove(), { once: true });
        window.setTimeout(() => toast.remove(), 400);
    };

    const startTimer = toast => {
        window.clearTimeout(Number(toast.dataset.timer));
        toast.dataset.timer = String(window.setTimeout(() => dismiss(toast), displayDurationMs));
    };

    const pause = toast => window.clearTimeout(Number(toast.dataset.timer));

    const resume = toast => {
        if (!toast.matches(":hover") && !toast.contains(document.activeElement)) {
            startTimer(toast);
        }
    };

    const toasts = Array.from(source.content.querySelectorAll("[data-toast]"));
    source.remove();

    // Insert after the initial render so screen readers announce the live region update.
    window.setTimeout(() => {
        for (const template of toasts) {
            const toast = document.importNode(template, true);
            toast.querySelector("[data-toast-close]")?.addEventListener("click", () => dismiss(toast));
            toast.addEventListener("mouseenter", () => pause(toast));
            toast.addEventListener("mouseleave", () => resume(toast));
            toast.addEventListener("focusin", () => pause(toast));
            toast.addEventListener("focusout", () => window.setTimeout(() => resume(toast)));
            list.append(toast);
            startTimer(toast);
        }

        window.lucide?.createIcons();
    }, 100);

    document.addEventListener("keydown", event => {
        if (event.key !== "Escape" || document.querySelector("dialog[open]")) {
            return;
        }

        const focused = list.querySelector("[data-toast]:focus-within");
        if (focused) {
            dismiss(focused);
        }
    });
})();
