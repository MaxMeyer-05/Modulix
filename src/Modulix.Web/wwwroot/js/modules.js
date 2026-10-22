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
        input.pattern = "/.*";
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

(() => {
    const section = document.querySelector("[data-module-logs-section]");
    if (!section) {
        return;
    }

    const wrapCheckbox = section.querySelector("[data-logs-wrap]");
    const outputEl = section.querySelector("[data-logs-output]");
    const emptyEl = section.querySelector("[data-logs-empty]");
    const containerEl = section.querySelector("[data-logs-container]");
    const errorEl = section.querySelector("[data-logs-error]");
    const errorTextEl = section.querySelector("[data-logs-error-text]");
    const lineCountEl = section.querySelector("[data-logs-line-count]");
    const refreshButton = section.querySelector("[data-refresh-logs]");
    const refreshLabel = section.querySelector("[data-refresh-logs-label]");
    const tailSelect = section.querySelector("[data-logs-tail-select]");

    if (wrapCheckbox && outputEl) {
        wrapCheckbox.addEventListener("change", () => {
            outputEl.classList.toggle("module-logs-wrap", wrapCheckbox.checked);
        });
    }

    const fetchLogs = async () => {
        if (!refreshButton) {
            return;
        }

        const baseEndpoint = refreshButton.getAttribute("data-refresh-logs");
        if (!baseEndpoint) {
            return;
        }

        const tailValue = tailSelect ? tailSelect.value : "500";
        const separator = baseEndpoint.includes("?") ? "&" : "?";
        const url = `${baseEndpoint}${separator}tail=${encodeURIComponent(tailValue)}`;

        if (refreshLabel) {
            refreshLabel.textContent = "Refreshing...";
        }
        refreshButton.setAttribute("aria-busy", "true");

        try {
            const response = await fetch(url, {
                headers: { "Accept": "application/json" }
            });
            const payload = await response.json().catch(() => null);

            if (!response.ok) {
                const message = payload?.error || `HTTP ${response.status}: Failed to load container logs.`;
                if (errorTextEl) {
                    errorTextEl.textContent = message;
                }
                if (errorEl) {
                    errorEl.hidden = false;
                }
                if (containerEl) {
                    containerEl.hidden = true;
                }
                return;
            }

            const logsText = typeof payload?.logs === "string" ? payload.logs : "";
            const count = typeof payload?.lineCount === "number" ? payload.lineCount : 0;

            if (errorEl) {
                errorEl.hidden = true;
            }
            if (containerEl) {
                containerEl.hidden = false;
            }
            if (lineCountEl) {
                lineCountEl.textContent = `${count} ${count === 1 ? "line" : "lines"}`;
            }

            const hasContent = logsText.trim().length > 0;
            if (emptyEl) {
                emptyEl.hidden = hasContent;
            }
            if (outputEl) {
                outputEl.textContent = logsText;
                outputEl.hidden = !hasContent;
            }
        } catch {
            if (errorTextEl) {
                errorTextEl.textContent = "The container logs could not be retrieved from the API.";
            }
            if (errorEl) {
                errorEl.hidden = false;
            }
            if (containerEl) {
                containerEl.hidden = true;
            }
        } finally {
            if (refreshLabel) {
                refreshLabel.textContent = "Refresh";
            }
            refreshButton.removeAttribute("aria-busy");
        }
    };

    if (refreshButton) {
        refreshButton.addEventListener("click", (event) => {
            event.preventDefault();
            fetchLogs();
        });
    }

    if (tailSelect) {
        tailSelect.addEventListener("change", () => {
            if (refreshButton && refreshButton.getAttribute("data-refresh-logs")) {
                fetchLogs();
            } else {
                const form = tailSelect.closest("form");
                if (form) {
                    form.submit();
                }
            }
        });
    }
})();


