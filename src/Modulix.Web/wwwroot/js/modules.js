(() => {
    const grid = document.getElementById("module-card-grid");
    if (grid) {
        const cards = Array.from(grid.querySelectorAll(".module-card"));
        const search = document.getElementById("module-search");
        const status = document.getElementById("module-status-filter");
        const sort = document.getElementById("module-sort");
        const updateCards = () => {
            const query = search.value.trim().toLowerCase();
            cards.sort((first, second) => {
                if (sort.value === "newest") {
                    return second.dataset.created.localeCompare(first.dataset.created);
                }
                const comparison = first.dataset.name.localeCompare(second.dataset.name);
                return sort.value === "name-desc" ? -comparison : comparison;
            });
            let visible = 0;
            for (const card of cards) {
                const text = [card.dataset.name, card.dataset.description, card.dataset.path, card.dataset.id].join(" ").toLowerCase();
                card.hidden = !text.includes(query) || (status.value !== "" && status.value !== card.dataset.status);
                if (!card.hidden) {
                    visible++;
                }
                grid.append(card);
            }
            document.getElementById("module-result-count").textContent = visible === cards.length
                ? `${visible} modules` : `${visible} of ${cards.length} modules`;
            document.getElementById("module-no-results").hidden = visible !== 0;
        };
        search.addEventListener("input", updateCards);
        status.addEventListener("change", updateCards);
        sort.addEventListener("change", updateCards);
        document.getElementById("reset-module-filters").addEventListener("click", () => {
            search.value = "";
            status.value = "";
            sort.value = "newest";
            updateCards();
            search.focus();
        });

        const dialog = document.getElementById("add-module-dialog");
        const resultDialog = document.getElementById("module-result-dialog");
        let isCreating = false;
        let creationSucceeded = false;
        document.getElementById("open-add-module").addEventListener("click", () => dialog.showModal());
        [dialog, resultDialog].forEach(target => {
            const close = () => {
                if (target !== dialog || !isCreating) {
                    target.close();
                }
            };
            target.querySelectorAll("[data-close-dialog]").forEach(button => button.addEventListener("click", close));
            target.addEventListener("cancel", event => {
                if (target === dialog && isCreating) {
                    event.preventDefault();
                }
            });
            target.addEventListener("click", event => {
                const bounds = target.getBoundingClientRect();
                if (event.target === target && (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom)) {
                    close();
                }
            });
        });
        resultDialog.addEventListener("close", () => {
            if (creationSucceeded) {
                location.reload();
            } else {
                dialog.showModal();
            }
        });
        const form = document.getElementById("add-module-form");
        const fields = document.getElementById("module-creation-fields");
        const progress = document.getElementById("module-creation-progress");
        const setCreating = busy => {
            isCreating = busy;
            fields.disabled = busy;
            dialog.querySelectorAll("button").forEach(button => button.disabled = busy);
            const submitButton = form.querySelector('[type="submit"]');
            submitButton.querySelector("span").textContent = busy ? "Creating..." : "Create";
            progress.hidden = !busy;
            form.setAttribute("aria-busy", String(busy));
        };
        const showResult = result => {
            creationSucceeded = result.statusCode === 201 || result.statusCode === 202;
            resultDialog.dataset.result = result.statusCode === 202 ? "accepted" : creationSucceeded ? "success" : "error";
            document.getElementById("module-result-heading").textContent = result.title;
            document.getElementById("module-result-code").textContent = result.statusCode === null
                ? "No HTTP response" : `HTTP ${result.statusCode}`;
            document.getElementById("module-result-description").textContent = result.description;
            const link = document.getElementById("module-result-link");
            link.hidden = !creationSucceeded || !result.moduleUrl;
            if (!link.hidden) {
                link.href = result.moduleUrl;
            } else {
                link.removeAttribute("href");
            }
            if (creationSucceeded) {
                form.reset();
                updateArchive();
            }
            dialog.close();
            resultDialog.showModal();
        };
        form.addEventListener("submit", async event => {
            event.preventDefault();
            if (isCreating) {
                return;
            }
            const body = new FormData(form);
            setCreating(true);
            let result;
            try {
                const response = await fetch(form.action, {
                    method: "POST",
                    body,
                    headers: { "Accept": "application/json" },
                    credentials: "same-origin"
                });
                const payload = await response.json().catch(() => null);
                result = response.redirected ? {
                    statusCode: 401,
                    title: "Sign in required",
                    description: "Your session may have expired. Please sign in again."
                } : {
                    statusCode: response.status,
                    title: payload?.title || response.statusText || "Module creation result",
                    description: payload?.description || (response.ok
                        ? "The server returned an unexpected response. Check the module list before retrying."
                        : "The server rejected the request. Check your session and upload, then try again."),
                    moduleUrl: payload?.moduleUrl
                };
            } catch {
                result = {
                    statusCode: null,
                    title: "Connection error",
                    description: "No response was received. Check your connection and the module list before retrying."
                };
            } finally {
                setCreating(false);
            }
            showResult(result);
        });
        const archive = document.getElementById("module-archive");
        const upload = document.getElementById("module-upload-area");
        const updateArchive = () => {
            const file = archive.files[0];
            archive.setCustomValidity(file && !file.name.toLowerCase().endsWith(".zip") ? "Choose a ZIP archive." : "");
            document.getElementById("module-file-name").textContent = file ? file.name : "Choose a ZIP archive";
            document.getElementById("module-file-size").textContent = file ? `${(file.size / 1024).toFixed(1)} KB` : ".zip";
        };
        archive.addEventListener("change", updateArchive);
        upload.addEventListener("dragover", event => {
            event.preventDefault();
            upload.classList.add("is-dragging");
        });
        upload.addEventListener("dragleave", () => upload.classList.remove("is-dragging"));
        upload.addEventListener("drop", event => {
            event.preventDefault();
            upload.classList.remove("is-dragging");
            if (event.dataTransfer.files.length === 1) {
                archive.files = event.dataTransfer.files;
                updateArchive();
            }
        });
    }

    document.querySelectorAll("[data-copy]").forEach(button => {
        button.addEventListener("click", async () => {
            const feedback = document.getElementById("module-copy-feedback");
            try {
                await navigator.clipboard.writeText(button.dataset.copy);
                button.title = "Copied";
                feedback.textContent = "Module ID copied.";
            } catch {
                feedback.textContent = "Could not copy the module ID.";
            }
        });
    });
})();