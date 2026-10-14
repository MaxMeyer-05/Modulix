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
