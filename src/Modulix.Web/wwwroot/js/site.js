(() => {
	const shell = document.getElementById("app-shell");
	const toggle = document.getElementById("sidebar-toggle");

	if (!shell || !toggle) {
		return;
	}

	const setCollapsed = (collapsed) => {
		shell.classList.toggle("sidebar-collapsed", collapsed);
		toggle.setAttribute("aria-expanded", String(!collapsed));
		const label = collapsed ? "Show menu" : "Hide menu";
		toggle.setAttribute("aria-label", label);
		toggle.title = label;
	};

	const storageKey = "modulix.sidebar.collapsed";
	const compactViewport = window.matchMedia("(max-width: 640px)");
	let initialCollapsed = compactViewport.matches;

	try {
		const storedState = window.localStorage.getItem(storageKey);
		if (storedState === "true" || storedState === "false") {
			initialCollapsed = storedState === "true";
		}
	} catch {
		initialCollapsed = compactViewport.matches;
	}

	setCollapsed(initialCollapsed);
	toggle.addEventListener("click", () => {
		const collapsed = !shell.classList.contains("sidebar-collapsed");
		setCollapsed(collapsed);

		try {
			window.localStorage.setItem(storageKey, String(collapsed));
		} catch {
			return;
		}
	});

	lucide.createIcons();
})();
