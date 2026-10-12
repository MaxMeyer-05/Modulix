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
	const today = new Date();
	const dateKey = (date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
	const formatDate = (date) => date.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
	const formatSize = (bytes) => `${(bytes / 1024).toFixed(1)} KB`;
	const templates = {
		Modulix: [
			["INFO", "Hosting", "Application starting. Environment: Development."],
			["INFO", "Database", "Database connection established."],
			["DEBUG", "Database", "Checking for pending migrations. Database is up to date."],
			["INFO", "DockerService", "Connected to Docker engine. API version: 1.47."],
			["INFO", "ModuleService", "Loading registered modules from the database."],
			["INFO", "ModuleService", "AnalyticsModule queued for endpoint scan."],
			["DEBUG", "EndpointScanner", "Creating isolated scanner container for AnalyticsModule."],
			["INFO", "EndpointScanner", "Scan completed. Discovered 8 endpoints in 1.24 s."],
			["WARN", "EndpointScanner", "ReportsModule: duplicate route /reports/export detected. Review required."],
			["INFO", "ModuleService", "AnalyticsModule endpoints confirmed."],
			["INFO", "DockerService", "Starting container modulix-analytics on port 8081."],
			["INFO", "ModuleService", "AnalyticsModule status changed: Starting -> Running."],
			["DEBUG", "RequestPipeline", "GET /api/modules completed with status 200 in 12 ms."],
			["INFO", "ModuleService", "NotificationService files updated. Scheduling a new scan."],
			["WARN", "DockerService", "Scanner memory usage exceeded 80% of the configured limit."],
			["ERROR", "DockerService", "Failed to start BillingService: port 8083 is already allocated.\nDockerApiException: driver failed programming external connectivity.\n   at DockerService.StartContainerAsync(CancellationToken cancellationToken)"],
			["INFO", "ModuleService", "BillingService status changed: Starting -> Failed."],
			["DEBUG", "RequestPipeline", "GET /api/modules/billing completed with status 200 in 8 ms."],
			["INFO", "DockerService", "Released unused port binding for BillingService."],
			["INFO", "ModuleService", "BillingService restarted successfully on port 8084."],
			["INFO", "EndpointScanner", "NotificationService scan completed. Discovered 5 endpoints."],
			["DEBUG", "Database", "Saved module status changes. 3 records updated."],
			["INFO", "ModuleService", "All pending module operations completed."],
			["INFO", "Hosting", "Health check passed. All services are ready."]
		],
		"Modulix.Web": [
			["INFO", "Hosting", "Web application starting. Environment: Development."],
			["INFO", "Authentication", "OpenID Connect configuration loaded."],
			["DEBUG", "Routing", "Razor page endpoints registered."],
			["INFO", "Hosting", "Now listening on: http://localhost:5038"],
			["INFO", "RequestPipeline", "GET / completed with status 200 in 42 ms."],
			["DEBUG", "ModulixClient", "Sending GET request to /api/modules."],
			["INFO", "ModulixClient", "Module list loaded successfully. 4 modules received."],
			["DEBUG", "Authentication", "Authentication cookie validated."],
			["INFO", "RequestPipeline", "GET /modules completed with status 200 in 28 ms."],
			["WARN", "ModulixClient", "Module detail request exceeded the expected response time: 1250 ms."],
			["INFO", "RequestPipeline", "GET /modules/detail completed with status 200 in 1268 ms."],
			["DEBUG", "TokenManagement", "Access token is valid. No refresh required."],
			["INFO", "RequestPipeline", "GET / completed with status 200 in 18 ms."],
			["DEBUG", "ModulixClient", "Sending GET request to /api/modules."],
			["INFO", "ModulixClient", "Module list loaded successfully. 4 modules received."],
			["INFO", "Hosting", "Health check passed. Web application is ready."]
		]
	};
	const files = [0, 1, 2, 3, 7, 14].flatMap(offset => {
		const date = new Date(today.getFullYear(), today.getMonth(), today.getDate() - offset);
		return Object.entries(templates).map(([service, events]) => {
			const key = dateKey(date);
			const name = `${service.toLowerCase().replace(".", "-")}-${key.replaceAll("-", "")}.log`;
			const entries = events.map(([level, source, message], index) => {
				const time = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 8, 12, index * 23);
				return { time: time.toLocaleTimeString("en-GB", { hour12: false }), level, source, message };
			});
			const text = entries.map(entry => `${key} ${entry.time} [${entry.level}] ${entry.source}: ${entry.message}`).join("\n") + "\n";
			return { name, key, date, service, entries, text, size: new TextEncoder().encode(text).length };
		});
	});
	let selectedFile = files[0];
	dateFilter.max = dateKey(today);
	document.getElementById("log-archive-range").textContent = `${formatDate(files.at(-1).date)} - ${formatDate(today)}`;

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
					badge.className = `log-badge log-badge-${entry.level.toLowerCase()}`;
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
		document.getElementById("log-empty-title").textContent = selectedFile ? "No matching entries" : "No log file selected";
		document.getElementById("log-reset-entries").hidden = !selectedFile;
		document.getElementById("log-result-count").textContent = `${entries.length} of ${selectedFile?.entries.length ?? 0} entries`;
		bottom.disabled = entries.length === 0;
		content.scrollTop = 0;
	};
	const renderSelectedFile = () => {
		const errors = selectedFile?.entries.filter(entry => entry.level === "ERROR").length ?? 0;
		const warnings = selectedFile?.entries.filter(entry => entry.level === "WARN").length ?? 0;
		document.getElementById("log-selected-name").textContent = selectedFile?.name ?? "Select a log file";
		document.getElementById("log-selected-meta").textContent = selectedFile
			? `${selectedFile.service} / ${formatDate(selectedFile.date)} / ${formatSize(selectedFile.size)}` : "No file selected";
		document.getElementById("log-total").textContent = selectedFile?.entries.length ?? 0;
		document.getElementById("log-errors").textContent = errors;
		document.getElementById("log-warnings").textContent = warnings;
		document.getElementById("log-error-label").textContent = errors === 1 ? "error" : "errors";
		document.getElementById("log-warning-label").textContent = warnings === 1 ? "warning" : "warnings";
		download.disabled = !selectedFile;
		search.disabled = !selectedFile;
		levelFilter.disabled = !selectedFile;
		for (const button of fileList.querySelectorAll("button")) {
			button.setAttribute("aria-current", String(button.dataset.file === selectedFile?.name));
		}
		renderEntries();
	};
	const renderFiles = () => {
		const matches = files.filter(file => (!serviceFilter.value || file.service === serviceFilter.value) && (!dateFilter.value || file.key === dateFilter.value));
		if (!matches.includes(selectedFile)) selectedFile = matches[0] ?? null;
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
			size.textContent = `${formatSize(file.size)} / ${file.entries.length} entries`;
			const errors = file.entries.filter(entry => entry.level === "ERROR").length;
			const state = document.createElement("span");
			state.className = errors ? "log-file-errors" : "";
			state.textContent = errors ? `${errors} ${errors === 1 ? "error" : "errors"}` : "No errors";
			facts.append(size, state);
			button.append(name, facts);
			button.addEventListener("click", () => {
				selectedFile = file;
				renderSelectedFile();
			});
			fileList.append(button);
		}
		document.getElementById("log-file-count").textContent = `${matches.length} files`;
		document.getElementById("log-archive-empty").hidden = matches.length !== 0;
		fileList.hidden = matches.length === 0;
		clearDate.disabled = !dateFilter.value;
		renderSelectedFile();
	};
	serviceFilter.addEventListener("change", renderFiles);
	dateFilter.addEventListener("change", renderFiles);
	clearDate.addEventListener("click", () => { dateFilter.value = ""; renderFiles(); });
	document.getElementById("log-reset-archive").addEventListener("click", () => {
		serviceFilter.value = "";
		dateFilter.value = "";
		renderFiles();
	});
	search.addEventListener("input", renderEntries);
	levelFilter.addEventListener("change", renderEntries);
	document.getElementById("log-reset-entries").addEventListener("click", () => {
		search.value = "";
		levelFilter.value = "";
		renderEntries();
		search.focus();
	});
	document.getElementById("log-wrap").addEventListener("change", event => table.classList.toggle("log-no-wrap", !event.target.checked));
	bottom.addEventListener("click", () => { content.scrollTop = content.scrollHeight; });
	download.addEventListener("click", () => {
		if (!selectedFile) return;
		const url = URL.createObjectURL(new Blob([selectedFile.text], { type: "text/plain;charset=utf-8" }));
		const link = document.createElement("a");
		link.href = url;
		link.download = selectedFile.name;
		link.click();
		setTimeout(() => URL.revokeObjectURL(url), 1000);
	});
	renderFiles();
})();