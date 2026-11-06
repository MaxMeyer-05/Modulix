(() => {
    const fieldErrors = new WeakMap();
    const pendingForms = new Map();
    const confirmationUpdates = new WeakMap();
    let nextErrorId = 0;

    const status = document.createElement("span");
    status.className = "visually-hidden";
    status.setAttribute("role", "status");
    document.body.appendChild(status);

    document.querySelectorAll("dialog").forEach(dialog => {
        const dialogStatus = document.createElement("span");
        dialogStatus.className = "visually-hidden";
        dialogStatus.setAttribute("role", "status");
        dialogStatus.dataset.formFeedbackStatus = "";
        dialog.appendChild(dialogStatus);
    });

    const isField = element => element instanceof HTMLInputElement
        || element instanceof HTMLSelectElement
        || element instanceof HTMLTextAreaElement;

    const ensureSummary = form => {
        const body = form.querySelector(".dialog-body");
        if (!body || body.querySelector(".module-form-error")) {
            return;
        }

        const summary = document.createElement("div");
        summary.className = "module-form-error";
        summary.dataset.validationSummary = "true";
        summary.setAttribute("role", "alert");
        summary.textContent = "Please correct the highlighted fields.";
        body.prepend(summary);
    };

    const showFieldError = (field, message) => {
        let state = fieldErrors.get(field);
        if (!state) {
            const error = document.createElement("span");
            error.id = `form-field-error-${++nextErrorId}`;
            error.className = "form-field-error";

            const row = field.closest("[data-subendpoint-row]");
            const upload = field.closest("[data-file-upload]");
            if (row) {
                row.appendChild(error);
            } else if (upload) {
                upload.insertAdjacentElement("afterend", error);
            } else {
                field.insertAdjacentElement("afterend", error);
            }

            state = {
                error,
                describedBy: field.getAttribute("aria-describedby"),
                invalid: field.getAttribute("aria-invalid")
            };
            fieldErrors.set(field, state);
            field.setAttribute("aria-describedby", [state.describedBy, error.id].filter(Boolean).join(" "));
        }

        state.error.textContent = message;
        field.setAttribute("aria-invalid", "true");
    };

    const clearFieldError = field => {
        const state = fieldErrors.get(field);
        if (!state) {
            return;
        }

        state.error.remove();
        if (state.describedBy === null) {
            field.removeAttribute("aria-describedby");
        } else {
            field.setAttribute("aria-describedby", state.describedBy);
        }
        if (state.invalid === null) {
            field.removeAttribute("aria-invalid");
        } else {
            field.setAttribute("aria-invalid", state.invalid);
        }
        fieldErrors.delete(field);

        const form = field.form;
        if (form && form.dataset.unmappedFieldErrors !== "true" && !form.querySelector(".form-field-error")) {
            form.querySelector('.module-form-error[data-validation-summary="true"]')?.remove();
        }
    };

    document.addEventListener("invalid", event => {
        const field = event.target;
        if (!isField(field) || !field.form?.closest("dialog")) {
            return;
        }

        showFieldError(field, field.validationMessage || "This field has an invalid value.");
        ensureSummary(field.form);
    }, true);

    for (const eventName of ["input", "change"]) {
        document.addEventListener(eventName, event => {
            if (isField(event.target)) {
                clearFieldError(event.target);
            }
        }, true);
    }

    document.addEventListener("click", event => {
        if (!(event.target instanceof Element)) {
            return;
        }
        const row = event.target.closest("[data-remove-subendpoint]")?.closest("[data-subendpoint-row]");
        row?.querySelectorAll("input, select, textarea").forEach(clearFieldError);
    }, true);

    const normalizeFieldName = name => name
        .replace(/^\$\./, "")
        .replace(/^(input|dto)\./i, "")
        .toLowerCase();

    document.querySelectorAll("form[data-field-errors]").forEach(form => {
        let errors;
        try {
            errors = JSON.parse(form.dataset.fieldErrors);
            if (!errors || typeof errors !== "object" || Array.isArray(errors)) {
                throw new SyntaxError("Field errors must be an object.");
            }
        } catch (error) {
            console.error("Could not read the form validation errors.", error);
            ensureSummary(form);
            return;
        }

        for (const [name, messages] of Object.entries(errors)) {
            const field = Array.from(form.elements).find(candidate =>
                isField(candidate)
                && candidate.type !== "hidden"
                && normalizeFieldName(candidate.name) === normalizeFieldName(name));
            if (!field || !Array.isArray(messages)) {
                form.dataset.unmappedFieldErrors = "true";
                continue;
            }

            const message = messages.filter(value => typeof value === "string" && value.trim()).join(" ");
            if (message) {
                showFieldError(field, message);
            }
        }
    });

    const formatFileSize = bytes => {
        if (bytes < 1024) {
            return `${bytes} B`;
        }
        if (bytes < 1024 * 1024) {
            return `${(bytes / 1024).toFixed(1)} KiB`;
        }
        if (bytes < 1024 * 1024 * 1024) {
            return `${(bytes / (1024 * 1024)).toFixed(1)} MiB`;
        }
        return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GiB`;
    };

    document.querySelectorAll("[data-file-upload]").forEach(area => {
        const input = area.querySelector('input[type="file"]');
        const details = area.querySelector("[data-file-details]");
        if (!(input instanceof HTMLInputElement) || !details || !input.form) {
            console.error("A file upload area must contain a file input, details, and a form.", area);
            return;
        }

        const showUploadError = message => {
            input.setCustomValidity(message);
            showFieldError(input, message);
            ensureSummary(input.form);
        };

        const updateFile = () => {
            const file = input.files?.[0];
            details.textContent = file ? `${file.name} (${formatFileSize(file.size)})` : "No file selected";
            input.setCustomValidity("");
            if (file && !file.name.toLowerCase().endsWith(".zip")) {
                showUploadError("Choose a ZIP archive.");
            }
        };

        input.addEventListener("change", updateFile);
        updateFile();

        const containsFiles = event => Array.from(event.dataTransfer?.types || []).includes("Files");
        for (const eventName of ["dragenter", "dragover"]) {
            area.addEventListener(eventName, event => {
                if (!containsFiles(event)) {
                    return;
                }
                event.preventDefault();
                event.dataTransfer.dropEffect = "copy";
                area.classList.add("is-dragging");
            });
        }

        area.addEventListener("dragleave", event => {
            if (!(event.relatedTarget instanceof Node) || !area.contains(event.relatedTarget)) {
                area.classList.remove("is-dragging");
            }
        });

        area.addEventListener("drop", event => {
            if (!containsFiles(event)) {
                return;
            }
            event.preventDefault();
            area.classList.remove("is-dragging");
            const files = event.dataTransfer.files;
            if (files.length !== 1) {
                input.value = "";
                details.textContent = "No file selected";
                showUploadError("Drop one ZIP archive.");
                return;
            }

            try {
                input.files = files;
            } catch (error) {
                if (!(error instanceof TypeError || error instanceof DOMException)) {
                    throw error;
                }
                console.error("Could not accept the dropped file.", error);
                input.value = "";
                details.textContent = "No file selected";
                showUploadError("This browser could not accept the dropped file. Choose the file instead.");
                return;
            }
            input.dispatchEvent(new Event("change", { bubbles: true }));
        });
    });

    document.querySelectorAll("[data-confirm-module-name]").forEach(input => {
        if (!(input instanceof HTMLInputElement) || !input.form) {
            return;
        }
        const button = input.form.querySelector("[data-confirm-delete]");
        if (!(button instanceof HTMLButtonElement)) {
            console.error("A module confirmation field must have a delete button.", input);
            return;
        }

        const updateConfirmation = () => {
            const matches = input.value === input.dataset.confirmModuleName;
            button.disabled = !matches || pendingForms.has(input.form);
            input.setCustomValidity(matches ? "" : "Enter the module name exactly as shown to confirm deletion.");
        };
        confirmationUpdates.set(input.form, updateConfirmation);
        input.addEventListener("input", updateConfirmation);
        updateConfirmation();
    });

    const restoreForm = form => {
        const state = pendingForms.get(form);
        if (!state) {
            return;
        }

        for (const { button, disabled } of state.buttons) {
            button.disabled = disabled;
        }
        if (state.submitter instanceof HTMLButtonElement) {
            state.submitter.replaceChildren(...state.children);
        } else if (state.submitter instanceof HTMLInputElement) {
            state.submitter.value = state.value;
            state.progress?.remove();
        }
        if (state.submitter) {
            if (state.label === null) {
                state.submitter.removeAttribute("aria-label");
            } else {
                state.submitter.setAttribute("aria-label", state.label);
            }
        }
        if (state.busy === null) {
            form.removeAttribute("aria-busy");
        } else {
            form.setAttribute("aria-busy", state.busy);
        }
        pendingForms.delete(form);
        confirmationUpdates.get(form)?.();
        if (state.announcement !== status) {
            state.announcement.textContent = "";
        }
        if (pendingForms.size === 0) {
            status.textContent = "";
        }
    };

    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || event.defaultPrevented) {
            return;
        }
        const submitter = event.submitter;
        const method = submitter?.getAttribute("formmethod") || form.method;
        if (method.toLowerCase() !== "post") {
            return;
        }
        if (pendingForms.has(form)) {
            event.preventDefault();
            return;
        }

        const buttons = Array.from(form.elements)
            .filter(element => (element instanceof HTMLButtonElement && element.type === "submit")
                || (element instanceof HTMLInputElement && ["submit", "image"].includes(element.type)))
            .map(button => ({ button, disabled: button.disabled }));
        const active = submitter || buttons.find(item => !item.disabled)?.button;
        const message = form.dataset.submitLabel || "Working...";
        const announcement = form.closest("dialog")?.querySelector("[data-form-feedback-status]") || status;
        const progress = document.createElement("span");
        progress.className = "form-submit-progress";
        const spinner = document.createElement("span");
        spinner.className = "form-submit-spinner";
        spinner.setAttribute("aria-hidden", "true");
        const label = document.createElement("span");
        label.textContent = message;
        progress.append(spinner, label);

        const state = {
            buttons,
            submitter: active,
            children: active instanceof HTMLButtonElement ? Array.from(active.childNodes) : [],
            value: active instanceof HTMLInputElement ? active.value : "",
            label: active?.getAttribute("aria-label") ?? null,
            busy: form.getAttribute("aria-busy"),
            progress,
            announcement
        };
        pendingForms.set(form, state);
        form.setAttribute("aria-busy", "true");
        if (active instanceof HTMLButtonElement) {
            active.replaceChildren(progress);
        } else if (active instanceof HTMLInputElement) {
            active.insertAdjacentElement("afterend", progress);
        }
        active?.setAttribute("aria-label", message);
        announcement.textContent = message;

        window.setTimeout(() => {
            if (pendingForms.get(form) !== state) {
                return;
            }
            if (event.defaultPrevented || !form.isConnected) {
                restoreForm(form);
                return;
            }
            if (active instanceof HTMLInputElement) {
                active.value = message;
            }
            for (const { button } of buttons) {
                button.disabled = true;
            }
        }, 0);
    });

    window.addEventListener("pageshow", event => {
        if (event.persisted) {
            for (const form of pendingForms.keys()) {
                restoreForm(form);
            }
            status.textContent = "";
        }
    });
})();
