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
        document.getElementById("open-add-module").addEventListener("click", () => dialog.showModal());
        dialog.querySelectorAll("[data-close-dialog]").forEach(button => button.addEventListener("click", () => dialog.close()));
        dialog.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                event.preventDefault();
                dialog.close();
            }
        });
        dialog.addEventListener("click", event => {
            const bounds = dialog.getBoundingClientRect();
            if (event.target === dialog && (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom)) {
                dialog.close();
            }
        });
        document.getElementById("add-module-form").addEventListener("submit", event => event.preventDefault());
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