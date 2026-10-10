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

	const searchInput = document.getElementById("workspace-search");
	const searchClear = document.getElementById("search-clear");
	const searchResults = document.getElementById("search-results");
	const searchResultList = document.getElementById("search-result-list");
	const searchEmpty = document.getElementById("search-empty");
	const notificationsToggle = document.getElementById("notifications-toggle");
	const notificationsPanel = document.getElementById("notifications-panel");
	const userMenuToggle = document.getElementById("user-menu-toggle");
	const userMenuPanel = document.getElementById("user-menu-panel");
	const signOutButton = document.getElementById("sign-out-button");
	const setUserMenuOpen = (open) => {
		userMenuPanel.hidden = !open;
		userMenuToggle.setAttribute("aria-expanded", String(open));
		if (open) {
			signOutButton.focus();
		}
	};
	const setNotificationsOpen = (open) => {
		notificationsPanel.hidden = !open;
		notificationsToggle.setAttribute("aria-expanded", String(open));
	};
	const updateSearch = () => {
		const query = searchInput.value.trim().toLowerCase();
		searchClear.hidden = searchInput.value.length === 0;
		searchResultList.replaceChildren();
		searchResults.hidden = query.length === 0;
		if (!query) {
			return;
		}

		setNotificationsOpen(false);
		setUserMenuOpen(false);
		const matches = Array.from(document.querySelectorAll(".sidebar-link"))
			.filter(entry => entry.textContent.trim().toLowerCase().includes(query));
		searchEmpty.hidden = matches.length !== 0;
		for (const entry of matches) {
			const item = document.createElement("li");
			const result = document.createElement(entry.matches("a") ? "a" : "div");
			result.className = "search-result";
			result.textContent = entry.textContent.trim();
			if (entry.matches("a")) {
				result.href = entry.href;
			} else {
				result.setAttribute("aria-disabled", "true");
				const availability = document.createElement("small");
				availability.textContent = "Not yet available";
				result.append(availability);
			}
			item.append(result);
			searchResultList.append(item);
		}
	};

	searchInput.addEventListener("input", updateSearch);
	searchInput.addEventListener("focus", updateSearch);
	searchInput.addEventListener("keydown", (event) => {
		if (event.key === "Enter" && !searchResults.hidden) {
			event.preventDefault();
			searchResultList.querySelector("a")?.click();
		}
	});
	searchClear.addEventListener("click", () => {
		searchInput.value = "";
		updateSearch();
		searchInput.focus();
	});
	notificationsToggle.addEventListener("click", () => {
		searchResults.hidden = true;
		setUserMenuOpen(false);
		setNotificationsOpen(notificationsPanel.hidden);
	});
	userMenuToggle.addEventListener("click", () => {
		searchResults.hidden = true;
		setNotificationsOpen(false);
		setUserMenuOpen(userMenuPanel.hidden);
	});
	userMenuToggle.addEventListener("keydown", (event) => {
		if (event.key === "ArrowDown") {
			event.preventDefault();
			searchResults.hidden = true;
			setNotificationsOpen(false);
			setUserMenuOpen(true);
		}
	});
	signOutButton.addEventListener("keydown", (event) => {
		if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
			event.preventDefault();
			signOutButton.focus();
		}
	});
	userMenuPanel.addEventListener("focusout", (event) => {
		if (!event.relatedTarget || !event.relatedTarget.closest(".user-menu")) {
			setUserMenuOpen(false);
		}
	});
	document.addEventListener("click", (event) => {
		if (!event.target.closest(".workspace-search")) {
			searchResults.hidden = true;
		}
		if (!event.target.closest(".notifications")) {
			setNotificationsOpen(false);
		}
		if (!event.target.closest(".user-menu")) {
			setUserMenuOpen(false);
		}
	});
	document.addEventListener("keydown", (event) => {
		if (event.key === "Escape") {
			if (!userMenuPanel.hidden) {
				setUserMenuOpen(false);
				userMenuToggle.focus();
			}
			if (!notificationsPanel.hidden) {
				setNotificationsOpen(false);
				notificationsToggle.focus();
			}
			if (!searchResults.hidden) {
				searchInput.focus();
				searchResults.hidden = true;
			}
		}
	});

	lucide.createIcons();
})();
