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

	const userMenuToggle = document.getElementById("user-menu-toggle");
	const userMenuPanel = document.getElementById("user-menu-panel");
	const signOutButton = document.getElementById("sign-out-button");
	if (userMenuToggle && userMenuPanel && signOutButton) {
		const setUserMenuOpen = (open) => {
			userMenuPanel.hidden = !open;
			userMenuToggle.setAttribute("aria-expanded", String(open));
			if (open) {
				signOutButton.focus();
			}
		};
		userMenuToggle.addEventListener("click", () => {
			setUserMenuOpen(userMenuPanel.hidden);
		});
		userMenuToggle.addEventListener("keydown", (event) => {
			if (event.key === "ArrowDown") {
				event.preventDefault();
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
			if (!event.target.closest(".user-menu")) {
				setUserMenuOpen(false);
			}
		});
		document.addEventListener("keydown", (event) => {
			if (event.key === "Escape" && !userMenuPanel.hidden) {
				setUserMenuOpen(false);
				userMenuToggle.focus();
			}
		});
	}

	lucide.createIcons();
})();

(() => {
	const returnFocus = new WeakMap();
	const triggers = [...document.querySelectorAll("[data-dialog-open]")];

	const openDialog = (dialog, trigger) => {
		if (dialog.open) {
			return;
		}

		returnFocus.set(dialog, trigger);
		dialog.showModal();
	};

	document.querySelectorAll("dialog").forEach(dialog => {
		dialog.addEventListener("close", () => {
			const trigger = returnFocus.get(dialog);
			if (trigger?.isConnected && !trigger.disabled) {
				trigger.focus();
			}
		});
	});

	document.querySelectorAll("dialog .dialog-body").forEach(body => {
		body.addEventListener("focusin", event => {
			const target = event.target;
			if (!(target instanceof HTMLElement)) {
				return;
			}

			requestAnimationFrame(() => {
				if (document.activeElement === target) {
					target.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "instant" });
				}
			});
		});
	});

	document.addEventListener("click", event => {
		if (!(event.target instanceof Element)) {
			return;
		}

		const trigger = event.target.closest("[data-dialog-open]");
		if (trigger) {
			const dialog = document.getElementById(trigger.dataset.dialogOpen);
			if (!(dialog instanceof HTMLDialogElement)) {
				console.error(`Dialog "${trigger.dataset.dialogOpen}" was not found.`);
				return;
			}

			event.preventDefault();
			openDialog(dialog, trigger);
			return;
		}

		const closeButton = event.target.closest("[data-dialog-close]");
		if (closeButton) {
			const dialog = closeButton.closest("dialog");
			if (!(dialog instanceof HTMLDialogElement)) {
				console.error("A dialog close button must be inside a dialog.");
				return;
			}

			event.preventDefault();
			dialog.close();
		}
	});

	document.querySelectorAll('dialog[data-open-on-load="true"]').forEach(dialog => {
		const trigger = document.getElementById(dialog.dataset.dialogReturnFocus)
			|| triggers.find(candidate => candidate.dataset.dialogOpen === dialog.id);
		openDialog(dialog, trigger);
	});
})();
