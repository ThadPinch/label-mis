// Dashboard charts + per-user layout customization. No dependencies.
// Data contract: window.DASHBOARD_DATA = { <seriesKey>: [{label, value, count?}, ...], ... }
(function () {
    "use strict";

    var DATA = window.DASHBOARD_DATA || {};
    var SVG_NS = "http://www.w3.org/2000/svg";
    var root = document.getElementById("dashRoot");
    if (!root) { return; }

    var styles = getComputedStyle(root);
    var C = {
        series: styles.getPropertyValue("--viz-series").trim() || "#005d66",
        seriesSoft: styles.getPropertyValue("--viz-series-soft").trim() || "rgba(0,93,102,.1)",
        grid: styles.getPropertyValue("--viz-grid").trim() || "#ececea",
        axis: styles.getPropertyValue("--viz-axis").trim() || "#c9c8c4",
        ink: styles.getPropertyValue("--viz-ink").trim() || "#171717",
        ink2: styles.getPropertyValue("--viz-ink-2").trim() || "#52514e",
        muted: styles.getPropertyValue("--viz-muted").trim() || "#898781",
        surface: styles.getPropertyValue("--viz-surface").trim() || "#ffffff",
        ordinal: [1, 2, 3, 4, 5].map(function (i) {
            return styles.getPropertyValue("--viz-ord-" + i).trim();
        })
    };

    // ---- formatting ----------------------------------------------------

    function fmtCurrency(v) {
        var abs = Math.abs(v);
        if (abs >= 1e6) { return "$" + trimNum(v / 1e6) + "M"; }
        if (abs >= 1e4) { return "$" + trimNum(v / 1e3) + "K"; }
        return "$" + Math.round(v).toLocaleString();
    }

    function fmtCurrencyFull(v) {
        return "$" + Math.round(v).toLocaleString();
    }

    function trimNum(v) {
        return (Math.round(v * 10) / 10).toLocaleString();
    }

    function fmtNumber(v) {
        return Math.round(v).toLocaleString();
    }

    function formatter(kind, full) {
        if (kind === "currency") { return full ? fmtCurrencyFull : fmtCurrency; }
        return fmtNumber;
    }

    // Clean axis maximum: 1/2/2.5/5 × 10^n at or above the data max.
    function niceMax(max) {
        if (max <= 0) { return 1; }
        var pow = Math.pow(10, Math.floor(Math.log10(max)));
        var candidates = [1, 2, 2.5, 5, 10];
        for (var i = 0; i < candidates.length; i++) {
            if (candidates[i] * pow >= max) { return candidates[i] * pow; }
        }
        return 10 * pow;
    }

    // ---- tooltip singleton ---------------------------------------------

    var tip = document.createElement("div");
    tip.className = "dash-tip";
    tip.setAttribute("role", "status");
    var tipTitle = document.createElement("div");
    tipTitle.className = "dash-tip-title";
    var tipRow = document.createElement("div");
    tipRow.className = "dash-tip-row";
    var tipKey = document.createElement("span");
    tipKey.className = "dash-tip-key";
    var tipValue = document.createElement("span");
    tipValue.className = "dash-tip-value";
    var tipLabel = document.createElement("span");
    tipLabel.className = "dash-tip-label";
    tipRow.appendChild(tipKey);
    tipRow.appendChild(tipValue);
    tipRow.appendChild(tipLabel);
    tip.appendChild(tipTitle);
    tip.appendChild(tipRow);
    document.body.appendChild(tip);

    function showTip(x, y, title, value, label, keyColor) {
        tipTitle.textContent = title;
        tipValue.textContent = value;
        tipLabel.textContent = label || "";
        tipKey.style.borderTopColor = keyColor || C.series;
        tip.classList.add("visible");
        var rect = tip.getBoundingClientRect();
        var left = Math.min(x + 14, window.innerWidth - rect.width - 8);
        var top = y - rect.height - 12;
        if (top < 8) { top = y + 16; }
        tip.style.left = left + "px";
        tip.style.top = top + "px";
    }

    function hideTip() {
        tip.classList.remove("visible");
    }

    // ---- svg helpers ---------------------------------------------------

    function el(name, attrs, parent) {
        var node = document.createElementNS(SVG_NS, name);
        for (var k in attrs) {
            if (Object.prototype.hasOwnProperty.call(attrs, k)) {
                node.setAttribute(k, attrs[k]);
            }
        }
        if (parent) { parent.appendChild(node); }
        return node;
    }

    function text(parent, x, y, str, attrs) {
        var node = el("text", Object.assign({
            x: x, y: y, "font-size": "11", fill: C.muted,
            "font-family": "inherit"
        }, attrs || {}), parent);
        node.textContent = str;
        return node;
    }

    function allZero(items) {
        return items.every(function (d) { return !d.value; });
    }

    function emptyState(container) {
        var div = document.createElement("div");
        div.className = "dash-empty";
        div.textContent = "No data for this period.";
        container.appendChild(div);
    }

    // Y-axis gridlines + tick labels. Returns y(value) scale.
    function yAxis(svg, plot, max, fmt) {
        var steps = 4;
        for (var i = 0; i <= steps; i++) {
            var v = max * i / steps;
            var y = plot.y + plot.h - (plot.h * i / steps);
            el("line", {
                x1: plot.x, x2: plot.x + plot.w, y1: y, y2: y,
                stroke: i === 0 ? C.axis : C.grid, "stroke-width": 1,
                "shape-rendering": "crispEdges"
            }, svg);
            text(svg, plot.x - 6, y + 3.5, fmt(v), { "text-anchor": "end" });
        }
        return function (v) { return plot.y + plot.h - (v / max) * plot.h; };
    }

    // ---- line chart (time series) --------------------------------------

    function renderLine(container, items, opts) {
        var w = container.clientWidth || 600;
        var h = 260;
        var plot = { x: 52, y: 14, w: w - 52 - 18, h: h - 14 - 30 };
        var svg = el("svg", { viewBox: "0 0 " + w + " " + h, width: w, height: h });
        var fmt = formatter(opts.format);
        var max = niceMax(Math.max.apply(null, items.map(function (d) { return d.value; })));
        var y = yAxis(svg, plot, max, fmt);
        var n = items.length;
        var x = function (i) {
            return n === 1 ? plot.x + plot.w / 2 : plot.x + (i / (n - 1)) * plot.w;
        };

        // x tick labels, thinned so they never collide
        var maxTicks = Math.max(2, Math.floor(plot.w / 64));
        var every = Math.ceil(n / maxTicks);
        items.forEach(function (d, i) {
            if (i % every === 0 || i === n - 1) {
                text(svg, x(i), plot.y + plot.h + 18, d.label, { "text-anchor": "middle" });
            }
        });

        var pts = items.map(function (d, i) { return [x(i), y(d.value)]; });
        var line = pts.map(function (p, i) {
            return (i === 0 ? "M" : "L") + p[0] + " " + p[1];
        }).join(" ");

        // area wash to the baseline
        el("path", {
            d: line + " L" + pts[n - 1][0] + " " + (plot.y + plot.h) +
               " L" + pts[0][0] + " " + (plot.y + plot.h) + " Z",
            fill: opts.color || C.series, opacity: "0.1", stroke: "none"
        }, svg);
        el("path", {
            d: line, fill: "none", stroke: opts.color || C.series,
            "stroke-width": 2, "stroke-linejoin": "round", "stroke-linecap": "round"
        }, svg);

        // end marker: ≥8px dot with a 2px surface ring, plus the endpoint direct label
        el("circle", {
            cx: pts[n - 1][0], cy: pts[n - 1][1], r: 4.5,
            fill: opts.color || C.series, stroke: C.surface, "stroke-width": 2
        }, svg);
        var endLabelX = Math.min(pts[n - 1][0], plot.x + plot.w - 4);
        text(svg, endLabelX, Math.max(pts[n - 1][1] - 10, 12), fmt(items[n - 1].value), {
            "text-anchor": "end", fill: C.ink, "font-weight": "600", "font-size": "12"
        });

        // crosshair + hover layer
        var cross = el("line", {
            x1: 0, x2: 0, y1: plot.y, y2: plot.y + plot.h,
            stroke: C.axis, "stroke-width": 1, opacity: "0",
            "shape-rendering": "crispEdges"
        }, svg);
        var hoverDot = el("circle", {
            r: 4.5, fill: opts.color || C.series, stroke: C.surface,
            "stroke-width": 2, opacity: "0"
        }, svg);

        var active = -1;
        function setActive(i, clientX, clientY) {
            active = i;
            if (i < 0) {
                cross.setAttribute("opacity", "0");
                hoverDot.setAttribute("opacity", "0");
                hideTip();
                return;
            }
            cross.setAttribute("x1", pts[i][0]);
            cross.setAttribute("x2", pts[i][0]);
            cross.setAttribute("opacity", "1");
            hoverDot.setAttribute("cx", pts[i][0]);
            hoverDot.setAttribute("cy", pts[i][1]);
            hoverDot.setAttribute("opacity", "1");
            var svgRect = svg.getBoundingClientRect();
            var px = clientX !== undefined ? clientX : svgRect.left + pts[i][0];
            var py = clientY !== undefined ? clientY : svgRect.top + pts[i][1];
            showTip(px, py, items[i].label, formatter(opts.format, true)(items[i].value),
                opts.name, opts.color || C.series);
        }

        var overlay = el("rect", {
            x: plot.x, y: plot.y, width: plot.w, height: plot.h,
            fill: "transparent"
        }, svg);
        overlay.addEventListener("pointermove", function (e) {
            var rect = svg.getBoundingClientRect();
            var mx = (e.clientX - rect.left) * (w / rect.width);
            var nearest = 0;
            var best = Infinity;
            for (var i = 0; i < n; i++) {
                var d = Math.abs(pts[i][0] - mx);
                if (d < best) { best = d; nearest = i; }
            }
            setActive(nearest, e.clientX, e.clientY);
        });
        overlay.addEventListener("pointerleave", function () { setActive(-1); });

        // keyboard: arrows walk the points, same readout as hover
        container.setAttribute("tabindex", "0");
        container.setAttribute("role", "img");
        container.setAttribute("aria-label", opts.name + " by period. Use arrow keys to read values; press T on the card's table button for a table view.");
        container.addEventListener("keydown", function (e) {
            if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") { return; }
            e.preventDefault();
            var next = active < 0 ? n - 1 : active + (e.key === "ArrowRight" ? 1 : -1);
            next = Math.max(0, Math.min(n - 1, next));
            setActive(next);
        });
        container.addEventListener("blur", function () { setActive(-1); });

        container.appendChild(svg);
    }

    // ---- column chart (time series, vertical bars) ---------------------

    function renderColumns(container, items, opts) {
        var w = container.clientWidth || 600;
        var h = 240;
        var plot = { x: 52, y: 14, w: w - 52 - 14, h: h - 14 - 30 };
        var svg = el("svg", { viewBox: "0 0 " + w + " " + h, width: w, height: h });
        var fmt = formatter(opts.format);
        var max = niceMax(Math.max.apply(null, items.map(function (d) { return d.value; })));
        var y = yAxis(svg, plot, max, fmt);
        var n = items.length;
        var band = plot.w / n;
        var barW = Math.min(band - 2, 24); // 2px surface gap between neighbors, capped thin
        var maxIdx = items.reduce(function (m, d, i) { return d.value > items[m].value ? i : m; }, 0);

        var maxTicks = Math.max(2, Math.floor(plot.w / 64));
        var every = Math.ceil(n / maxTicks);

        items.forEach(function (d, i) {
            var cx = plot.x + band * i + band / 2;
            if (i % every === 0 || i === n - 1) {
                text(svg, cx, plot.y + plot.h + 18, d.label, { "text-anchor": "middle" });
            }
            var top = y(d.value);
            var bh = plot.y + plot.h - top;
            var r = Math.min(4, barW / 2, bh); // rounded data-end, square baseline
            var x0 = cx - barW / 2;
            var bar;
            if (bh > 0) {
                bar = el("path", {
                    d: "M" + x0 + " " + (plot.y + plot.h) +
                       " V" + (top + r) +
                       " Q" + x0 + " " + top + " " + (x0 + r) + " " + top +
                       " H" + (x0 + barW - r) +
                       " Q" + (x0 + barW) + " " + top + " " + (x0 + barW) + " " + (top + r) +
                       " V" + (plot.y + plot.h) + " Z",
                    fill: opts.color || C.series
                }, svg);
            }
            if (i === maxIdx && d.value > 0) {
                text(svg, cx, top - 6, fmt(d.value), {
                    "text-anchor": "middle", fill: C.ink, "font-weight": "600", "font-size": "12"
                });
            }

            // hit target spans the full band, not just the painted bar
            var hit = el("rect", {
                x: plot.x + band * i, y: plot.y, width: band, height: plot.h,
                fill: "transparent"
            }, svg);
            hit.setAttribute("tabindex", "0");
            hit.setAttribute("role", "img");
            hit.setAttribute("aria-label", d.label + ": " + formatter(opts.format, true)(d.value));
            function over(e) {
                if (bar) { bar.setAttribute("opacity", "0.8"); }
                var rect = hit.getBoundingClientRect();
                showTip(e && e.clientX !== undefined ? e.clientX : rect.left + rect.width / 2,
                    e && e.clientY !== undefined ? e.clientY : rect.top,
                    d.label, formatter(opts.format, true)(d.value), opts.name,
                    opts.color || C.series);
            }
            function out() {
                if (bar) { bar.removeAttribute("opacity"); }
                hideTip();
            }
            hit.addEventListener("pointermove", over);
            hit.addEventListener("pointerleave", out);
            hit.addEventListener("focus", function () { over(null); });
            hit.addEventListener("blur", out);
        });

        container.appendChild(svg);
    }

    // ---- horizontal bar chart (categories) -----------------------------

    function renderBars(container, items, opts) {
        var w = container.clientWidth || 400;
        var rowH = 30;
        var padTop = 4;
        var h = items.length * rowH + padTop + 6;
        var labelW = Math.min(Math.max(w * 0.3, 80), 150);
        var valueW = 52;
        var plot = { x: labelW + 8, y: padTop, w: w - labelW - 8 - valueW, h: items.length * rowH };
        var svg = el("svg", { viewBox: "0 0 " + w + " " + h, width: w, height: h });
        var fmt = formatter(opts.format);
        var max = Math.max.apply(null, items.map(function (d) { return d.value; }));
        if (max <= 0) { max = 1; }
        var barH = Math.min(rowH - 12, 18);

        // baseline
        el("line", {
            x1: plot.x, x2: plot.x, y1: plot.y, y2: plot.y + plot.h,
            stroke: C.axis, "stroke-width": 1, "shape-rendering": "crispEdges"
        }, svg);

        items.forEach(function (d, i) {
            var cy = plot.y + rowH * i + rowH / 2;
            var color = opts.ramp === "ordinal"
                ? C.ordinal[Math.min(i, C.ordinal.length - 1)]
                : (opts.color || C.series);

            // category label, truncated to fit its gutter
            var lbl = text(svg, labelW, cy + 4, d.label, {
                "text-anchor": "end", fill: C.ink2, "font-size": "12"
            });
            truncateSvgText(lbl, d.label, labelW - 6);

            var bw = Math.max((d.value / max) * plot.w, 0);
            var r = Math.min(4, barH / 2, bw);
            var bar = null;
            if (bw > 0) {
                bar = el("path", {
                    d: "M" + plot.x + " " + (cy - barH / 2) +
                       " H" + (plot.x + bw - r) +
                       " Q" + (plot.x + bw) + " " + (cy - barH / 2) + " " + (plot.x + bw) + " " + (cy - barH / 2 + r) +
                       " V" + (cy + barH / 2 - r) +
                       " Q" + (plot.x + bw) + " " + (cy + barH / 2) + " " + (plot.x + bw - r) + " " + (cy + barH / 2) +
                       " H" + plot.x + " Z",
                    fill: color
                }, svg);
            }

            // value at the tip
            text(svg, plot.x + bw + 6, cy + 4, fmt(d.value), {
                fill: C.ink, "font-weight": "600", "font-size": "12"
            });

            var hit = el("rect", {
                x: 0, y: plot.y + rowH * i, width: w, height: rowH, fill: "transparent"
            }, svg);
            hit.setAttribute("tabindex", "0");
            hit.setAttribute("role", "img");
            hit.setAttribute("aria-label", d.label + ": " + formatter(opts.format, true)(d.value));
            function over(e) {
                if (bar) { bar.setAttribute("opacity", "0.8"); }
                var rect = hit.getBoundingClientRect();
                var extra = typeof d.count === "number" && d.count !== d.value
                    ? opts.name + " · " + d.count + (d.count === 1 ? " item" : " items")
                    : opts.name;
                showTip(e && e.clientX !== undefined ? e.clientX : rect.left + rect.width / 2,
                    e && e.clientY !== undefined ? e.clientY : rect.top,
                    d.label, formatter(opts.format, true)(d.value), extra, color);
            }
            function out() {
                if (bar) { bar.removeAttribute("opacity"); }
                hideTip();
            }
            hit.addEventListener("pointermove", over);
            hit.addEventListener("pointerleave", out);
            hit.addEventListener("focus", function () { over(null); });
            hit.addEventListener("blur", out);
        });

        container.appendChild(svg);
    }

    function truncateSvgText(node, full, maxWidth) {
        if (node.getComputedTextLength() <= maxWidth) { return; }
        var str = full;
        while (str.length > 1 && node.getComputedTextLength() > maxWidth) {
            str = str.slice(0, -1);
            node.textContent = str + "…";
        }
        var title = document.createElementNS(SVG_NS, "title");
        title.textContent = full;
        node.appendChild(title);
    }

    // ---- chart bootstrapping + table view ------------------------------

    function buildTable(items, opts) {
        var wrap = document.createElement("div");
        wrap.className = "dash-table-wrap";
        var table = document.createElement("table");
        table.className = "dash-table";
        var thead = document.createElement("thead");
        var hr = document.createElement("tr");
        [opts.col, opts.name].forEach(function (name, i) {
            var th = document.createElement("th");
            th.scope = "col";
            th.textContent = name;
            if (i === 1) { th.className = "num"; }
            hr.appendChild(th);
        });
        thead.appendChild(hr);
        table.appendChild(thead);
        var tbody = document.createElement("tbody");
        var fmtFull = formatter(opts.format, true);
        items.forEach(function (d) {
            var tr = document.createElement("tr");
            var td1 = document.createElement("td");
            td1.textContent = d.label;
            var td2 = document.createElement("td");
            td2.className = "num";
            td2.textContent = fmtFull(d.value);
            tr.appendChild(td1);
            tr.appendChild(td2);
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        wrap.appendChild(table);
        return wrap;
    }

    var renderers = { line: renderLine, columns: renderColumns, bars: renderBars };

    document.querySelectorAll(".dash-chart[data-chart]").forEach(function (container) {
        var kind = container.getAttribute("data-chart");
        var items = DATA[container.getAttribute("data-series")];
        var opts = {
            format: container.getAttribute("data-format") || "number",
            name: container.getAttribute("data-name") || "Value",
            col: container.getAttribute("data-col") || "Category",
            ramp: container.getAttribute("data-ramp") || ""
        };

        if (!items || !items.length || allZero(items)) {
            emptyState(container);
            var card0 = container.closest(".dash-card");
            var toggle0 = card0 && card0.querySelector(".dash-table-toggle");
            if (toggle0) { toggle0.hidden = true; }
            return;
        }

        function draw() {
            container.textContent = "";
            renderers[kind](container, items, opts);
        }
        draw();

        // redraw on container resize (debounced)
        var raf = null;
        var lastW = container.clientWidth;
        new ResizeObserver(function () {
            if (container.clientWidth === lastW || container.hidden) { return; }
            lastW = container.clientWidth;
            if (raf) { cancelAnimationFrame(raf); }
            raf = requestAnimationFrame(draw);
        }).observe(container);

        // table-view toggle — the chart's accessibility twin
        var card = container.closest(".dash-card");
        var toggle = card && card.querySelector(".dash-table-toggle");
        if (toggle) {
            toggle.innerHTML =
                '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">' +
                '<rect x="1.5" y="2.5" width="13" height="11" rx="1"/>' +
                '<path d="M1.5 6h13M6 6v7.5M10.5 6v7.5"/></svg>';
            var tableView = null;
            toggle.addEventListener("click", function () {
                var pressed = toggle.getAttribute("aria-pressed") === "true";
                if (!tableView) {
                    tableView = buildTable(items, opts);
                    container.parentNode.insertBefore(tableView, container.nextSibling);
                }
                tableView.hidden = pressed;
                container.hidden = !pressed;
                toggle.setAttribute("aria-pressed", String(!pressed));
                if (pressed) { draw(); }
            });
        }
    });

    // ---- customization: hide/show + drag reorder, per user -------------

    var STORAGE_KEY = "lsdash:v1:" + (root.getAttribute("data-user") || "anon");
    var zones = Array.prototype.slice.call(root.querySelectorAll("[data-dash-zone]"));
    var customizeBtn = document.getElementById("dashCustomizeBtn");
    var tray = document.getElementById("dashTray");
    var trayItems = document.getElementById("dashTrayItems");
    var resetBtn = document.getElementById("dashResetBtn");
    var editing = false;

    function loadState() {
        try {
            return JSON.parse(localStorage.getItem(STORAGE_KEY)) || { hidden: [], order: {} };
        } catch (e) {
            return { hidden: [], order: {} };
        }
    }

    function saveState(state) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(state)); } catch (e) { /* private mode */ }
    }

    var state = loadState();

    function widgetsIn(zone) {
        return Array.prototype.slice.call(zone.querySelectorAll("[data-widget]"));
    }

    function applyState() {
        zones.forEach(function (zone) {
            var name = zone.getAttribute("data-dash-zone");
            var saved = state.order[name];
            if (saved && saved.length) {
                saved.forEach(function (id) {
                    var node = zone.querySelector('[data-widget="' + CSS.escape(id) + '"]');
                    if (node) { zone.appendChild(node); }
                });
            }
            widgetsIn(zone).forEach(function (node) {
                node.hidden = state.hidden.indexOf(node.getAttribute("data-widget")) !== -1;
            });
        });
        renderTray();
    }

    function captureOrder() {
        zones.forEach(function (zone) {
            state.order[zone.getAttribute("data-dash-zone")] = widgetsIn(zone).map(function (node) {
                return node.getAttribute("data-widget");
            });
        });
        saveState(state);
    }

    function renderTray() {
        trayItems.textContent = "";
        state.hidden.forEach(function (id) {
            var node = root.querySelector('[data-widget="' + CSS.escape(id) + '"]');
            if (!node) { return; }
            var chip = document.createElement("button");
            chip.type = "button";
            chip.className = "dash-tray-chip";
            chip.textContent = "+ " + (node.getAttribute("data-title") || id);
            chip.addEventListener("click", function () {
                state.hidden = state.hidden.filter(function (x) { return x !== id; });
                saveState(state);
                applyState();
            });
            trayItems.appendChild(chip);
        });
        tray.hidden = !editing;
        tray.querySelector(".dash-tray-label").textContent = state.hidden.length > 0
            ? "Hidden widgets — click to restore:"
            : "No hidden widgets. Use the × on a widget to hide it.";
    }

    function hideWidget(id) {
        if (state.hidden.indexOf(id) === -1) { state.hidden.push(id); }
        saveState(state);
        applyState();
    }

    // Hide buttons are injected only while editing.
    function addHideButtons() {
        zones.forEach(function (zone) {
            widgetsIn(zone).forEach(function (node) {
                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "dash-iconbtn dash-hide-btn";
                btn.title = "Hide widget";
                btn.setAttribute("aria-label", "Hide " + (node.getAttribute("data-title") || "widget"));
                btn.textContent = "×";
                btn.addEventListener("click", function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    hideWidget(node.getAttribute("data-widget"));
                });
                var actions = node.querySelector(".dash-card-actions");
                if (!actions) {
                    var head = node.querySelector(".dash-card-head");
                    if (head) {
                        actions = document.createElement("div");
                        actions.className = "dash-card-actions";
                        actions.setAttribute("data-dash-edit-only", "");
                        head.appendChild(actions);
                    }
                }
                if (actions) { actions.appendChild(btn); } else { node.appendChild(btn); }
                btn.setAttribute("data-dash-edit-only", "");
            });
        });
    }

    function removeHideButtons() {
        root.querySelectorAll("[data-dash-edit-only]").forEach(function (n) { n.remove(); });
    }

    var dragged = null;

    function enableDrag(on) {
        zones.forEach(function (zone) {
            widgetsIn(zone).forEach(function (node) {
                node.draggable = on;
            });
        });
    }

    zones.forEach(function (zone) {
        zone.addEventListener("dragstart", function (e) {
            if (!editing) { return; }
            var target = e.target.closest && e.target.closest("[data-widget]");
            if (!target) { return; }
            dragged = target;
            target.classList.add("dash-dragging");
            e.dataTransfer.effectAllowed = "move";
            try { e.dataTransfer.setData("text/plain", target.getAttribute("data-widget")); } catch (err) { /* IE */ }
        });
        zone.addEventListener("dragend", function () {
            if (dragged) { dragged.classList.remove("dash-dragging"); }
            zone.querySelectorAll(".dash-drag-over").forEach(function (n) {
                n.classList.remove("dash-drag-over");
            });
            dragged = null;
        });
        zone.addEventListener("dragover", function (e) {
            if (!editing || !dragged || dragged.parentNode !== zone) { return; }
            e.preventDefault();
            e.dataTransfer.dropEffect = "move";
            var over = e.target.closest && e.target.closest("[data-widget]");
            zone.querySelectorAll(".dash-drag-over").forEach(function (n) {
                n.classList.remove("dash-drag-over");
            });
            if (over && over !== dragged) { over.classList.add("dash-drag-over"); }
        });
        zone.addEventListener("drop", function (e) {
            if (!editing || !dragged || dragged.parentNode !== zone) { return; }
            e.preventDefault();
            var over = e.target.closest && e.target.closest("[data-widget]");
            if (over && over !== dragged) {
                var widgets = widgetsIn(zone);
                var from = widgets.indexOf(dragged);
                var to = widgets.indexOf(over);
                if (from < to) {
                    zone.insertBefore(dragged, over.nextSibling);
                } else {
                    zone.insertBefore(dragged, over);
                }
                captureOrder();
            }
            zone.querySelectorAll(".dash-drag-over").forEach(function (n) {
                n.classList.remove("dash-drag-over");
            });
        });
        // In edit mode, tiles are links — block navigation clicks.
        zone.addEventListener("click", function (e) {
            if (editing && e.target.closest && e.target.closest("a[data-widget]")) {
                e.preventDefault();
            }
        });
    });

    customizeBtn.addEventListener("click", function () {
        editing = !editing;
        root.classList.toggle("dash-editing", editing);
        customizeBtn.setAttribute("aria-pressed", String(editing));
        customizeBtn.textContent = editing ? "Done" : "Customize";
        tray.hidden = !editing;
        enableDrag(editing);
        if (editing) { addHideButtons(); renderTray(); } else { removeHideButtons(); }
    });

    resetBtn.addEventListener("click", function () {
        localStorage.removeItem(STORAGE_KEY);
        window.location.reload();
    });

    applyState();
    tray.hidden = true;
})();
