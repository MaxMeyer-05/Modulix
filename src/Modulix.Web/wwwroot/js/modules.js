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

document.querySelectorAll("[data-page-size-select]").forEach(select => {
    select.addEventListener("change", () => {
        const form = select.closest("form");
        if (form) {
            form.submit();
        }
    });
});

document.querySelectorAll("[data-edit-version]").forEach(button => {
    button.addEventListener("click", () => {
        const version = button.dataset.editVersion || "";
        const changelog = button.dataset.editChangelog || "";
        const originalInput = document.getElementById("edit-version-original");
        const nameInput = document.getElementById("edit-version-name");
        const changelogInput = document.getElementById("edit-version-changelog");

        if (originalInput) {
            originalInput.value = version;
        }
        if (nameInput) {
            nameInput.value = version;
        }
        if (changelogInput) {
            changelogInput.value = changelog;
        }
    });
});

document.querySelectorAll("[data-delete-version]").forEach(button => {
    button.addEventListener("click", () => {
        const version = button.dataset.deleteVersion || "";
        const targetInput = document.getElementById("delete-version-target");
        const label = document.getElementById("delete-version-label");

        if (targetInput) {
            targetInput.value = version;
        }
        if (label) {
            label.textContent = version;
        }
    });
});

(() => {
    const container = document.querySelector("[data-auto-refresh]");
    if (!container) {
        return;
    }

    const intervalSeconds = Number.parseInt(container.dataset.autoRefresh || "300", 10);
    if (!Number.isFinite(intervalSeconds) || intervalSeconds <= 0) {
        return;
    }

    const countdownEl = container.querySelector("[data-auto-refresh-countdown]");
    const refreshLink = container.querySelector("[data-auto-refresh-link]");
    let remainingSeconds = intervalSeconds;

    const formatDuration = (totalSeconds) => {
        if (totalSeconds < 60) {
            return `${totalSeconds}s`;
        }

        const minutes = Math.floor(totalSeconds / 60);
        const seconds = totalSeconds % 60;
        return seconds === 0 ? `${minutes}m` : `${minutes}m ${seconds}s`;
    };

    const updateLabel = (text) => {
        if (countdownEl) {
            countdownEl.textContent = text;
        }
    };

    const isEditingFilter = () => {
        const openDialog = document.querySelector(".module-dialog.module-dialog-open, .module-dialog:popover-open");
        if (openDialog) {
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

        updateLabel(`Auto-refresh in ${formatDuration(remainingSeconds)}`);
    }, 1000);
})();

(() => {
    const addButton = document.getElementById("add-subendpoint-button");
    const list = document.getElementById("initial-endpoints-list");
    const form = document.getElementById("add-module-form");

    if (!addButton || !list) {
        return;
    }

    const methods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    const reindexRows = () => {
        list.querySelectorAll("[data-subendpoint-row]").forEach((row, index) => {
            const select = row.querySelector("select");
            const input = row.querySelector("input");
            if (select) {
                select.name = `InitialEndpoints[${index}].HttpMethod`;
            }
            if (input) {
                input.name = `InitialEndpoints[${index}].EndpointPath`;
            }
        });
    };

    const createRow = (index) => {
        const row = document.createElement("div");
        row.className = "module-subendpoint-row";
        row.setAttribute("data-subendpoint-row", "");

        const select = document.createElement("select");
        select.name = `InitialEndpoints[${index}].HttpMethod`;
        select.setAttribute("aria-label", "HTTP method");
        methods.forEach((method) => {
            const option = document.createElement("option");
            option.value = method;
            option.textContent = method;
            select.appendChild(option);
        });

        const input = document.createElement("input");
        input.name = `InitialEndpoints[${index}].EndpointPath`;
        input.required = true;
        input.maxLength = 200;
        input.placeholder = "/api/v1/items";
        input.setAttribute("aria-label", "Endpoint path");

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "module-icon-button module-subendpoint-remove";
        removeButton.setAttribute("data-remove-subendpoint", "");
        removeButton.setAttribute("aria-label", "Remove endpoint");
        removeButton.title = "Remove endpoint";

        const icon = document.createElement("i");
        icon.setAttribute("data-lucide", "trash-2");
        icon.setAttribute("aria-hidden", "true");
        removeButton.appendChild(icon);

        row.append(select, input, removeButton);
        return { row, input };
    };

    addButton.addEventListener("click", () => {
        const nextIndex = list.querySelectorAll("[data-subendpoint-row]").length;
        const { row, input } = createRow(nextIndex);
        list.appendChild(row);
        if (window.lucide && typeof window.lucide.createIcons === "function") {
            window.lucide.createIcons();
        }
        input.focus();
    });

    list.addEventListener("click", (event) => {
        const removeButton = event.target.closest("[data-remove-subendpoint]");
        if (!removeButton) {
            return;
        }

        const row = removeButton.closest("[data-subendpoint-row]");
        if (row) {
            row.remove();
            reindexRows();
        }
    });

    if (form) {
        form.addEventListener("submit", () => {
            reindexRows();
        });
    }
})();


