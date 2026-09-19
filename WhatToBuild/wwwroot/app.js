"use strict";

const ITEM_SLOTS = 6;

const POSITIONS = {
    TOP: "Top",
    JUNGLE: "Jungle",
    MIDDLE: "Mid",
    BOTTOM: "Bot",
    UTILITY: "Support",
};


const ICONS = {
    tower: '<svg viewBox="0 0 16 16"><path d="M3 2h2v2h2V2h2v2h2V2h2v4l-1 1v6h1v2H3v-2h1V7L3 6z"/></svg>',
    inhib: '<svg viewBox="0 0 16 16"><path d="M8 1l6 7-6 7-6-7z M8 5L5 8l3 3 3-3z" fill-rule="evenodd"/></svg>',
    baron: '<svg viewBox="0 0 16 16"><path d="M8 2c3 0 6 2.5 6 6 0 2-1 3-2 4l1 2h-3l-1-2H7L6 14H3l1-2C3 11 2 10 2 8c0-3.5 3-6 6-6zm-2.5 5a1 1 0 100 2 1 1 0 000-2zm5 0a1 1 0 100 2 1 1 0 000-2z"/></svg>',
    herald: '<svg viewBox="0 0 16 16"><path d="M8 3c4 0 7 5 7 5s-3 5-7 5-7-5-7-5 3-5 7-5zm0 2.5a2.5 2.5 0 100 5 2.5 2.5 0 000-5z"/></svg>',
};

const $ = (id) => document.getElementById(id);

function esc(value) {
    return String(value ?? "").replace(/[&<>"']/g, (c) => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
    })[c]);
}

function clock(seconds) {
    const s = Math.max(0, Math.floor(seconds));
    return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
}

function gold(value) {
    return value >= 1000 ? `${(value / 1000).toFixed(1)}k` : `${Math.round(value)}`;
}

function setStatus(kind, text) {
    const el = $("status");
    el.className = `status status-${kind}`;
    el.textContent = text;
}

function renderObjectives(team) {
    const o = team.objectives;
    const pips = [];

    for (const dragon of o.dragons) {
        for (let i = 0; i < dragon.count; i++) {
            pips.push(`<img class="drake" src="img/dragons/${esc(dragon.type)}.png" alt="${esc(dragon.name)}" title="${esc(dragon.name)}" />`);
        }
    }

    for (let i = 0; i < o.elders; i++) {
        pips.push('<img class="drake" src="img/dragons/Elder.png" alt="Elder Dragon" title="Elder Dragon" />');
    }

    const drakeCount = o.dragons.reduce((sum, d) => sum + d.count, 0);
    for (let i = drakeCount; i < 4 && !o.soulType; i++) {
        pips.push('<span class="drake drake-empty"></span>');
    }

    const soul = o.soulType
        ? `<span class="soul drake-${esc(o.soulType)}"><img src="img/dragons/${esc(o.soulType)}.png" alt="" />${esc(o.soulName ?? o.soulType)}</span>`
        : "";

    return `
        <div class="objectives">
            <span class="dragons" title="Dragons">${pips.join("")}</span>
            ${soul}
            <span class="objective" title="Turrets destroyed">${ICONS.tower}<b>${o.turrets}</b></span>
            <span class="objective" title="Inhibitors destroyed">${ICONS.inhib}<b>${o.inhibitors}</b></span>
            <span class="objective" title="Rift Heralds">${ICONS.herald}<b>${o.heralds}</b></span>
            <span class="objective" title="Barons">${ICONS.baron}<b>${o.barons}</b></span>
        </div>`;
}

function renderItems(items) {
    const slots = items.slice(0, ITEM_SLOTS).map((item) => `
        <div class="slot" ${itemAttrs(item)}>
            <img src="${esc(item.icon)}" alt="${esc(item.name)}" loading="lazy" />
            ${item.count > 1 ? `<span class="count">${item.count}</span>` : ""}
        </div>`);

    while (slots.length < ITEM_SLOTS) {
        slots.push('<div class="slot"></div>');
    }

    return `<div class="items">${slots.join("")}</div>`;
}

function renderStats(player) {
    const s = player.stats;
    const cls = player.statsAreExact ? "stat stat-exact" : "stat";
    const note = player.statsAreExact ? "" : '<span class="estimate-note" title="Rebuilt from level, items and dragons">estimated</span>';

    const entries = [
        ["HP", Math.round(s.health)],
        ["AD", Math.round(s.attackDamage)],
        ["AP", Math.round(s.abilityPower)],
        ["Armor", Math.round(s.armor)],
        ["MR", Math.round(s.magicResist)],
        ["AS", s.attackSpeed.toFixed(2)],
    ];

    if (s.tenacity > 0) {
        entries.push(["Ten", `${Math.round(s.tenacity * 100)}%`]);
    }

    return `
        <div class="stats">
            ${entries.map(([label, value]) => `<span class="${cls}">${label}<b>${value}</b></span>`).join("")}
            ${note}
        </div>`;
}


function renderPlayer(player) {
    const classes = ["player"];
    if (player.isActivePlayer) classes.push("player-you");
    if (player.isDead) classes.push("player-dead");

    return `
        <li class="${classes.join(" ")}">
            <div class="portrait">
                <img src="${esc(player.championIcon)}" alt="${esc(player.champion)}" />
                ${player.isDead ? '<span class="dead-tag">DEAD</span>' : ""}
                <span class="level">${player.level}</span>
            </div>
            <div class="identity">
                <div class="champion">${esc(player.champion)}</div>
                <div class="role">
                    ${esc(POSITIONS[player.position] ?? player.position)}

                </div>

            </div>
            <div class="score">
                <div class="kda">${player.kills} / <span class="d">${player.deaths}</span> / ${player.assists}</div>
                <div class="cs">${player.creepScore} CS · ${gold(player.itemValue)}</div>
            </div>
            ${renderItems(player.items)}
            ${renderStats(player)}
        </li>`;
}

function renderTeam(team, element) {
    const title = team.team === "Order" ? "Blue side" : "Red side";

    element.innerHTML = `
        <div class="team-head">
            <span class="team-title">${title}</span>
            <span class="team-sub">
                <span>KDA <b>${team.kills} / ${team.deaths} / ${team.assists}</b></span>
                <span>Items <b>${gold(team.itemValue)}</b></span>
            </span>
        </div>
        ${renderObjectives(team)}
        <ul class="players">${team.players.map(renderPlayer).join("")}</ul>`;
}

function render(state) {
    lastState = state;
    renderBuild();
    $("patch").textContent = state.patch ? `Patch ${state.patch}` : "";

    if (state.phase !== "InGame") {
        $("game").hidden = true;
        $("waiting").hidden = false;
        return;
    }

    $("waiting").hidden = true;
    $("game").hidden = false;

    const order = state.teams.find((t) => t.team === "Order");
    const chaos = state.teams.find((t) => t.team === "Chaos");

    $("clock").textContent = clock(state.gameTime);
    $("order-kills").textContent = order?.kills ?? 0;
    $("chaos-kills").textContent = chaos?.kills ?? 0;


    if (order) renderTeam(order, $("team-order"));
    if (chaos) renderTeam(chaos, $("team-chaos"));

    const unknown = [
        ...state.unknownChampions.map((c) => `champion "${c}"`),
        ...state.unknownItemIds.map((id) => `item ${id}`),
    ];

    $("unknown").hidden = unknown.length === 0;
    $("unknown").textContent = unknown.length ? `Not in our data: ${unknown.join(", ")}` : "";
}

const tipItems = new Map();
const tipWhy = new Map();

let lastState = null;
let lastRecommendation = null;

function itemAttrs(item, whyKey) {
    tipItems.set(item.riotId, item);
    return `data-item="${item.riotId}"${whyKey ? ` data-why="${whyKey}"` : ""}`;
}

function renderTip(target) {
    const item = tipItems.get(Number(target.dataset.item));
    if (!item) {
        return "";
    }

    const why = tipWhy.get(target.dataset.why) ?? [];
    const list = (lines, cls) => lines.length
        ? `<ul class="${cls}">${lines.map((l) => `<li>${esc(l)}</li>`).join("")}</ul>`
        : "";

    return `
        <div class="tip-head">
            <img src="${esc(item.icon)}" alt="" />
            <div>
                <div class="tip-name">${esc(item.name)}</div>
                <div class="tip-cost">${item.cost} gold</div>
            </div>
        </div>
        ${list(item.stats, "tip-stats")}
        ${list(item.effects, "tip-effects")}
        ${why.length ? `<div class="tip-label">Why</div>${list(why, "tip-why")}` : ""}`;
}

function showTip(target) {
    const tip = $("tip");
    const html = renderTip(target);
    if (!html) {
        return;
    }

    tip.innerHTML = html;
    tip.hidden = false;

    const rect = target.getBoundingClientRect();
    const width = tip.offsetWidth;
    const height = tip.offsetHeight;
    const left = Math.min(Math.max(8, rect.left + rect.width / 2 - width / 2), window.innerWidth - width - 8);
    const above = rect.top - height - 8;
    const top = above >= 8 ? above : rect.bottom + 8;

    tip.style.left = `${left + window.scrollX}px`;
    tip.style.top = `${top + window.scrollY}px`;
}

function hideTip() {
    $("tip").hidden = true;
}

document.addEventListener("mouseover", (e) => {
    const target = e.target.closest("[data-item]");
    if (target) showTip(target); else hideTip();
});

document.addEventListener("click", (e) => {
    const target = e.target.closest("[data-item]");
    if (target) showTip(target); else hideTip();
});

function eta(step, gameTime) {
    if (step.status === "Owned") {
        return "Built";
    }

    if (step.etaSeconds == null) {
        return "";
    }

    if (step.etaSeconds <= gameTime + 1) {
        return "Now";
    }

    return `~${clock(step.etaSeconds)} <span class="spread">± ${clock(step.etaSpreadSeconds ?? 0)}</span>`;
}

function renderRecall(buyNow) {
    if (!buyNow.target) {
        return `<p class="recall-summary">${esc(buyNow.summary)}</p>`;
    }

    const slots = buyNow.items.map((item) => `
        <div class="slot slot-lg" ${itemAttrs(item)}>
            <img src="${esc(item.icon)}" alt="${esc(item.name)}" loading="lazy" />
        </div>`).join("");

    const cost = buyNow.items.length
        ? `<p class="recall-cost">${buyNow.cost} gold · ${Math.floor(buyNow.goldLeft)} left</p>`
        : "";

    const why = buyNow.why.length
        ? `<ul class="recall-why">${buyNow.why.map((w) => `<li>${esc(w)}</li>`).join("")}</ul>`
        : "";

    return `
        ${slots ? `<div class="recall-items">${slots}</div>` : ""}
        <p class="recall-summary">${esc(buyNow.summary)}</p>
        ${cost}
        ${why}`;
}

function renderPath(steps, gameTime) {
    return steps.map((step, i) => {
        const key = `step-${i}`;
        tipWhy.set(key, step.status === "Owned" ? [] : step.why);

        return `
            <li class="step step-${step.status.toLowerCase()}" ${itemAttrs(step.item, key)}>
                <div class="slot slot-lg"><img src="${esc(step.item.icon)}" alt="${esc(step.item.name)}" loading="lazy" /></div>
                <div class="step-name">${esc(step.item.name)}</div>
                <div class="step-eta">${eta(step, gameTime)}</div>
            </li>`;
    }).join("");
}

function renderSkipped(skipped) {
    if (!skipped.length) {
        return "";
    }

    return `
        <ul class="skipped">
            ${skipped.map((s) => `
                <li>
                    <div class="slot slot-sm" ${itemAttrs(s.item)}><img src="${esc(s.item.icon)}" alt="${esc(s.item.name)}" loading="lazy" /></div>
                    <span><b>${esc(s.item.name)}</b> ${esc(s.reason)}</span>
                </li>`).join("")}
        </ul>`;
}

function percentGain(before, after) {
    return before > 0 ? Math.round((after / before - 1) * 100) : 0;
}

function ttk(seconds) {
    return seconds == null ? "30s+" : `${seconds.toFixed(1)}s`;
}

const SPLIT_COLORS = ["#c8aa6e", "#0ac8b9", "#3a9fe0", "#e84057", "#b58cff", "#93d14b", "#a09b8c"];

function renderSplit(split) {
    if (!split || split.length < 2) {
        return "";
    }

    const bars = split.map((s, i) =>
        `<span style="width:${(s.share * 100).toFixed(1)}%;background:${SPLIT_COLORS[i % SPLIT_COLORS.length]}" title="${esc(s.source)} ${Math.round(s.share * 100)}%"></span>`).join("");

    const legend = split.map((s, i) =>
        `<span class="split-key"><i style="background:${SPLIT_COLORS[i % SPLIT_COLORS.length]}"></i>${esc(s.source)} <b>${Math.round(s.share * 100)}%</b></span>`).join("");

    return `
        <div class="split">
            <div class="build-label">Where your damage comes from after buying</div>
            <div class="split-bar">${bars}</div>
            <div class="split-legend">${legend}</div>
        </div>`;
}

function renderImpact(impact) {
    if (!impact || !impact.perEnemy.length) {
        return "";
    }

    const best = Math.max(...impact.perEnemy.map((e) => percentGain(e.dpsBefore, e.dpsAfter)), 1);

    const rows = impact.perEnemy.map((e) => {
        const gain = percentGain(e.dpsBefore, e.dpsAfter);
        return `
            <tr>
                <td class="impact-champ"><img src="${esc(e.icon)}" alt="" />${esc(e.champion)}</td>
                <td class="impact-dps">${ttk(e.ttkBefore)} → <b>${ttk(e.ttkAfter)}</b></td>
                <td class="impact-dps">${Math.round(e.dpsBefore)} → <b>${Math.round(e.dpsAfter)}</b></td>
                <td class="impact-bar"><span style="width:${Math.max(4, (gain / best) * 100)}%"></span><em>+${gain}%</em></td>
                <td class="impact-note">${esc(e.note)}</td>
            </tr>`;
    }).join("");

    return `
        <table class="impact">
            <thead><tr><th>Enemy</th><th>Time to kill</th><th>DPS</th><th>Gain</th><th>Their stats</th></tr></thead>
            <tbody>${rows}</tbody>
        </table>
        ${renderSplit(impact.split)}`;
}

function renderCastHints(hints) {
    if (!hints || !hints.length) {
        return "";
    }

    const rows = hints.map((h) => {
        const share = Math.min(100, (h.castAtHealth / h.maxHealth) * 100);
        const kill = Math.min(100, (h.killingHealth / h.maxHealth) * 100);
        return `
            <li class="hint">
                <img src="${esc(h.icon)}" alt="" />
                <div class="hint-main">
                    <div class="hint-line"><b>${esc(h.ability)}</b> on ${esc(h.champion)} at <b>${Math.round(h.castAtHealth)}</b> HP <span class="hint-of">of ${Math.round(h.maxHealth)}</span></div>
                    <div class="hint-bar" title="Cast at ${Math.round(h.castAtHealth)}, the pounce kills from ${Math.round(h.killingHealth)}">
                        <span class="hint-cast" style="width:${share}%"></span>
                        <span class="hint-kill" style="width:${kill}%"></span>
                    </div>
                    ${h.additions.length ? `<div class="hint-extra">${h.additions.map(esc).join(" · ")}</div>` : ""}
                </div>
            </li>`;
    }).join("");

    return `
        <div class="cast-hints">
            <div class="build-label">When to cast, with your current items</div>
            <ul class="hints">${rows}</ul>
        </div>`;
}

function renderNeeds(needs, gameTime) {
    if (!needs.length) {
        return "";
    }

    const items = needs.map((n) => {
        const covered = n.coveredBy
            ? `<span class="need-covered">Covered by ${esc(n.coveredBy)}${n.coveredAtSeconds != null && n.coveredAtSeconds > gameTime + 1 ? ` at ~${clock(n.coveredAtSeconds)}` : ""}</span>`
            : `<span class="need-open">Not in the build</span>${n.options.length ? `<span class="need-options">Options:
                   ${n.options.map((o) => `<span class="slot slot-sm" ${itemAttrs(o)}><img src="${esc(o.icon)}" alt="${esc(o.name)}" loading="lazy" /></span>`).join("")}
               </span>` : ""}`;

        return `
            <li class="need ${n.coveredBy ? "need-ok" : "need-missing"}">
                <div class="need-name">${esc(n.need)}</div>
                <div class="need-detail">${esc(n.detail)}</div>
                <div class="need-status">${covered}</div>
            </li>`;
    }).join("");

    return `
        <div class="team-needs">
            <div class="build-label">What the enemy team calls for</div>
            <ul class="needs">${items}</ul>
        </div>`;
}

function renderBuild() {
    const el = $("build");
    const state = lastState;

    if (!state || state.phase !== "InGame" || !state.activeTeam) {
        el.hidden = true;
        return;
    }

    el.hidden = false;
    tipWhy.clear();

    const rec = lastRecommendation;
    const perMinute = rec?.goldPerMinute != null ? `<span>Income<strong>${Math.round(rec.goldPerMinute)}/min</strong></span>` : "";

    const head = `
        <div class="build-head">
            <span class="build-title">Your build ${rec?.isSample ? '<span class="sample">Sample</span>' : ""}</span>
            <span class="purse">
                <span>Gold<strong>${Math.floor(state.currentGold)}</strong></span>
                <span>Earned<strong>${gold(state.goldEarned)}</strong></span>
                ${perMinute}
            </span>
        </div>`;

    if (!rec) {
        el.innerHTML = `${head}<p class="build-empty">No build recommendations yet.</p>`;
        return;
    }

    const next = rec.buildPath.find((s) => s.status === "Next");
    const why = next
        ? `<div class="next-why">
               <div class="build-label">Why ${esc(next.item.name)} next</div>
               <ul>${next.why.map((w) => `<li>${esc(w)}</li>`).join("")}</ul>
               ${renderImpact(next.impact)}
           </div>`
        : "";

    el.innerHTML = `
        ${head}
        <div class="build-grid">
            <div class="recall">
                <div class="build-label">On recall</div>
                ${renderRecall(rec.buyNow)}
            </div>
            <div class="path">
                <div class="build-label">Build path</div>
                <ol class="steps">${renderPath(rec.buildPath, state.gameTime)}</ol>
                ${renderSkipped(rec.skipped)}
            </div>
        </div>
        ${why}
        ${renderCastHints(rec.castHints)}
        ${renderNeeds(rec.teamNeeds, state.gameTime)}
        <ul class="assumptions">${rec.assumptions.map((a) => `<li>${esc(a)}</li>`).join("")}</ul>`;
}

function renderRecommendation(recommendation) {
    lastRecommendation = recommendation;
    renderBuild();
}

async function loadInitial() {
    try {
        const response = await fetch("/api/state");
        if (response.ok) {
            render(await response.json());
        }

        const recommendation = await fetch("/api/recommendation");
        const text = recommendation.ok ? await recommendation.text() : "";
        renderRecommendation(text ? JSON.parse(text) : null);
    } catch {
    }
}

async function connect() {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hub")
        .withAutomaticReconnect()
        .build();

    connection.on("GameState", render);
    connection.on("Recommendation", renderRecommendation);
    connection.onreconnecting(() => setStatus("reconnecting", "Reconnecting"));
    connection.onreconnected(() => setStatus("live", "Connected"));
    connection.onclose(() => setStatus("offline", "Offline"));

    while (true) {
        try {
            await connection.start();
            setStatus("live", "Connected");
            return;
        } catch {
            setStatus("offline", "Offline");
            await new Promise((resolve) => setTimeout(resolve, 3000));
        }
    }
}

loadInitial();
connect();
