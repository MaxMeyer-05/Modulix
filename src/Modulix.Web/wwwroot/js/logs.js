(() => {
	const fileList = document.getElementById("log-file-list");
	if (!fileList) return;

	const serviceFilter = document.getElementById("log-service");
	const dateFilter = document.getElementById("log-date");
	const clearDate = document.getElementById("log-clear-date");
	const search = document.getElementById("log-search");
	const levelFilter = document.getElementById("log-level");
	const entriesBody = document.getElementById("log-entries");
	const content = document.getElementById("log-content");
	const table = document.getElementById("log-table");
	const download = document.getElementById("log-download");
	const bottom = document.getElementById("log-bottom");
	const logPage = fileList.closest(".logs-page");
	const archiveMessage = document.getElementById("log-archive-message");
	const resetArchive = document.getElementById("log-reset-archive");
	const resetEntries = document.getElementById("log-reset-entries");
	const today = new Date();
	const dateKey = (date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
	const formatDate = (date) => date.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
	const formatSize = (bytes) => `${(bytes / 1024).toFixed(1)} KB`;
	const levels = { VRB: "TRACE", DBG: "DEBUG", INF: "INFO", WRN: "WARN", ERR: "ERROR", FTL: "FATAL" };
	const isError = (entry) => entry.level === "ERROR" || entry.level === "FATAL";
	let files = [];
	let selectedFile = null;
	let loadingFiles = false;
	let loadingFile = false;
	let archiveError = "";
	let readError = "";
	let readController = null;
	dateFilter.max = dateKey(today);

	const parseEntries = (text) => {
		const entries = [];
		const header = /^\[(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2}(?:\.\d+)?) [^|]*\|\[([A-Z]{3})\]\|(.*?)\|([^|]*)\|(.*)\|$/;
		for (const line of text.split(/\r?\n/)) {
			const match = header.exec(line);
			if (match) {
				entries.push({
					time: match[2],
					level: levels[match[3]] ?? "UNKNOWN",
					source: match[5],
					message: match[4] ? `${match[6]}\n${match[4]}` : match[6]
				});
			} else if (entries.length) {
				entries.at(-1).message += `\n${line}`;
			} else if (line.trim()) {
				entries.push({ time: "", level: "UNKNOWN", source: "", message: line });
			}
		}
		return entries;
	};

	const fetchLog = async (endpoint, name, signal) => {
		const url = new URL(endpoint, window.location.href);
		if (name) url.searchParams.set("name", name);
		const response = await fetch(url, { cache: "no-store", redirect: "error", signal });
		if (!response.ok) {
			const messages = {
				401: "Please sign in again to view logs.",
				403: "You do not have permission to view these logs.",
				404: "This log file no longer exists.",
				413: "This log file exceeds the 1 MiB read limit.",
				503: "The log file is temporarily unavailable."
			};
			throw new Error(messages[response.status] ?? `Could not load logs (HTTP ${response.status}).`);
		}
		return response;
	};

	const renderEntries = () => {
		const query = search.value.trim().toLowerCase();
		const entries = (selectedFile?.entries ?? []).filter(entry =>
			(!levelFilter.value || entry.level === levelFilter.value) &&
			`${entry.time} ${entry.level} ${entry.source} ${entry.message}`.toLowerCase().includes(query));
		const fragment = document.createDocumentFragment();
		for (const entry of entries) {
			const row = document.createElement("tr");
			for (const [className, value] of [["log-time", entry.time], ["log-level-cell", entry.level], ["log-source", entry.source], ["log-message", entry.message]]) {
				const cell = document.createElement("td");
				cell.className = className;
				if (className === "log-level-cell") {
					const badge = document.createElement("span");
					const badgeLevel = entry.level === "FATAL" ? "error" : entry.level === "TRACE" ? "debug" : entry.level.toLowerCase();
					badge.className = `log-badge log-badge-${badgeLevel}`;
					badge.textContent = value;
					cell.append(badge);
				} else {
					cell.textContent = value;
				}
				row.append(cell);
			}
			fragment.append(row);
		}
		entriesBody.replaceChildren(fragment);
		table.hidden = entries.length === 0;
		document.getElementById("log-empty").hidden = entries.length !== 0;
		document.getElementById("log-empty-title").textContent = loadingFiles || loadingFile ? "Loading logs..."
			: readError || (selectedFile ? (selectedFile.entries?.length ? "No matching entries" : "This log file is empty") : "No log file selected");
		resetEntries.hidden = !selectedFile || loadingFile || (!readError && !search.value && !levelFilter.value);
		resetEntries.textContent = readError ? "Retry" : "Clear search and level filter";
		document.getElementById("log-result-count").textContent = loadingFile ? "Loading entries..."
			: `${entries.length} of ${selectedFile?.entries?.length ?? 0} entries`;
		bottom.disabled = entries.length === 0;
		content.setAttribute("aria-busy", String(loadingFile));
		content.scrollTop = 0;
	};
	const renderSelectedFile = () => {
		const errors = selectedFile?.entries?.filter(isError).length ?? 0;
		const warnings = selectedFile?.entries?.filter(entry => entry.level === "WARN").length ?? 0;
		document.getElementById("log-selected-name").textContent = selectedFile?.name ?? "Select a log file";
		document.getElementById("log-selected-meta").textContent = selectedFile
			? `${selectedFile.service} / ${formatDate(selectedFile.date)} / ${formatSize(selectedFile.size)}` : "No file selected";
		document.getElementById("log-total").textContent = selectedFile?.entries?.length ?? 0;
		document.getElementById("log-errors").textContent = errors;
		document.getElementById("log-warnings").textContent = warnings;
		document.getElementById("log-error-label").textContent = errors === 1 ? "error" : "errors";
		document.getElementById("log-warning-label").textContent = warnings === 1 ? "warning" : "warnings";
		const loaded = selectedFile?.text != null;
		download.disabled = !loaded;
		search.disabled = !loaded;
		levelFilter.disabled = !loaded;
		for (const button of fileList.querySelectorAll("button")) {
			button.setAttribute("aria-current", String(button.dataset.file === selectedFile?.name));
		}
		renderEntries();
	};
	const selectFile = async (file) => {
		readController?.abort();
		selectedFile = file;
		readError = "";
		loadingFile = !!file && file.text === null;
		renderSelectedFile();
		if (!loadingFile) return;
		const controller = new AbortController();
		readController = controller;
		try {
			const response = await fetchLog(logPage.dataset.readUrl, file.name, controller.signal);
			if (!response.headers.get("content-type")?.includes("text/plain")) {
				throw new Error("Please sign in again to view logs.");
			}
			const text = await response.text();
			if (controller.signal.aborted) return;
			file.text = text;
			file.entries = parseEntries(text);
		} catch (error) {
			if (controller.signal.aborted) return;
			readError = error instanceof TypeError ? "Could not load logs. Check your connection and sign-in session." : error.message;
		} finally {
			if (!controller.signal.aborted) {
				loadingFile = false;
				renderFiles();
			}
		}
	};
	const renderFiles = () => {
		const matches = files.filter(file => (!serviceFilter.value || file.service === serviceFilter.value) && (!dateFilter.value || file.key === dateFilter.value));
		if (!matches.includes(selectedFile)) void selectFile(matches[0] ?? null);
		fileList.replaceChildren();
		let lastDate = "";
		for (const file of matches) {
			if (file.key !== lastDate) {
				const heading = document.createElement("h3");
				heading.className = "log-file-group";
				heading.textContent = file.key === dateKey(today) ? `Today / ${formatDate(file.date)}` : formatDate(file.date);
				fileList.append(heading);
				lastDate = file.key;
			}
			const button = document.createElement("button");
			button.type = "button";
			button.className = "log-file-button";
			button.dataset.file = file.name;
			const name = document.createElement("strong");
			name.textContent = file.name;
			const facts = document.createElement("span");
			facts.className = "log-file-facts";
			const size = document.createElement("span");
			size.textContent = `${formatSize(file.size)}${file.entries ? ` / ${file.entries.length} entries` : ""}`;
			const errors = file.entries?.filter(isError).length ?? 0;
			const state = document.createElement("span");
			state.className = errors ? "log-file-errors" : "";
			state.textContent = file.entries ? (errors ? `${errors} ${errors === 1 ? "error" : "errors"}` : "No errors") : "";
			facts.append(size, state);
			button.append(name, facts);
			button.addEventListener("click", () => {
				void selectFile(file);
			});
			fileList.append(button);
		}
		document.getElementById("log-file-count").textContent = loadingFiles ? "Loading..." : `${matches.length} files`;
		document.getElementById("log-archive-empty").hidden = loadingFiles || matches.length !== 0;
		archiveMessage.textContent = archiveError || "No files for this selection.";
		resetArchive.textContent = archiveError ? "Retry" : "Reset filters";
		fileList.hidden = matches.length === 0;
		fileList.setAttribute("aria-busy", String(loadingFiles));
		clearDate.disabled = !dateFilter.value;
		renderSelectedFile();
	};
	const loadFiles = async () => {
		loadingFiles = true;
		archiveError = "";
		renderFiles();
		try {
			const response = await fetchLog(logPage.dataset.filesUrl);
			if (!response.headers.get("content-type")?.includes("application/json")) {
				throw new Error("Please sign in again to view logs.");
			}
			const metadata = await response.json();
			files = metadata.map(file => {
				const match = /^log-(\d{4})(\d{2})(\d{2})(?:_\d{3})?\.log$/.exec(file.name);
				if (!match) return null;
				return {
					name: file.name,
					size: file.size,
					key: `${match[1]}-${match[2]}-${match[3]}`,
					date: new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3])),
					service: "Modulix.Web",
					entries: null,
					text: null
				};
			}).filter(Boolean).sort((first, second) => second.name.localeCompare(first.name));
			document.getElementById("log-archive-range").textContent = files.length
				? `${formatDate(files.at(-1).date)} - ${formatDate(files[0].date)}` : "No log files";
		} catch (error) {
			archiveError = error instanceof TypeError ? "Could not load logs. Check your connection and sign-in session." : error.message;
		} finally {
			loadingFiles = false;
			renderFiles();
		}
	};
	serviceFilter.addEventListener("change", renderFiles);
	dateFilter.addEventListener("change", renderFiles);
	clearDate.addEventListener("click", () => { dateFilter.value = ""; renderFiles(); });
	resetArchive.addEventListener("click", () => {
		if (archiveError) {
			void loadFiles();
			return;
		}
		serviceFilter.value = "";
		dateFilter.value = "";
		renderFiles();
	});
	search.addEventListener("input", renderEntries);
	levelFilter.addEventListener("change", renderEntries);
	resetEntries.addEventListener("click", () => {
		if (readError) {
			void selectFile(selectedFile);
			return;
		}
		search.value = "";
		levelFilter.value = "";
		renderEntries();
		search.focus();
	});
	document.getElementById("log-wrap").addEventListener("change", event => table.classList.toggle("log-no-wrap", !event.target.checked));
	bottom.addEventListener("click", () => { content.scrollTop = content.scrollHeight; });
	download.addEventListener("click", () => {
		if (selectedFile?.text == null) return;
		const url = URL.createObjectURL(new Blob([selectedFile.text], { type: "text/plain;charset=utf-8" }));
		const link = document.createElement("a");
		link.href = url;
		link.download = selectedFile.name;
		link.click();
		setTimeout(() => URL.revokeObjectURL(url), 1000);
	});
	void loadFiles();
})();