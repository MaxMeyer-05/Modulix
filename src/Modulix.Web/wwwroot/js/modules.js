document.querySelectorAll("[data-copy]").forEach(button => {
    button.addEventListener("click", async () => {
        const feedback = document.getElementById("module-copy-feedback");
        try {
            await navigator.clipboard.writeText(button.dataset.copy);
            button.title = "Copied";
            if (feedback) {
                feedback.textContent = "ID copied.";
            }
        } catch {
            if (feedback) {
                feedback.textContent = "Could not copy the ID.";
            }
        }
    });
});

(() => {
    const container = document.querySelector("[data-auto-refresh]");
    if (!container) {
        return;
    }

    const intervalSeconds = Number.parseInt(container.dataset.autoRefresh || "60", 10);
    if (!Number.isFinite(intervalSeconds) || intervalSeconds <= 0) {
        return;
    }

    const countdownEl = container.querySelector("[data-auto-refresh-countdown]");
    const refreshLink = container.querySelector("[data-auto-refresh-link]");
    let remainingSeconds = intervalSeconds;

    const updateLabel = (text) => {
        if (countdownEl) {
            countdownEl.textContent = text;
        }
    };

    const isEditingFilter = () => {
        const addDialog = document.getElementById("add-module-dialog");
        if (addDialog && (addDialog.classList.contains("module-dialog-open") || addDialog.matches(":popover-open"))) {
            return true;
        }

        const active = document.activeElement;
        return Boolean(active && active.closest(".modules-toolbar, .module-dialog") && active.matches("input, select, textarea"));
    };

    window.setInterval(() => {
        if (document.hidden) {
            return;
        }

        if (isEditingFilter()) {
            updateLabel("Auto-refresh paused");
            return;
        }

        remainingSeconds -= 1;
        if (remainingSeconds <= 0) {
            updateLabel("Refreshing...");
            const targetUrl = refreshLink?.getAttribute("href") || window.location.href;
            window.location.replace(targetUrl);
            return;
        }

        updateLabel(`Auto-refresh in ${remainingSeconds}s`);
    }, 1000);
})();

