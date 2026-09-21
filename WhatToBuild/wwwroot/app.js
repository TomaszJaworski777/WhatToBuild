"use strict";

const ITEM_SLOTS = 6;
const TRINKET_SLOT = 6;
const WHY_LINES = 5;

const POSITIONS = {
    TOP: "Top",
    JUNGLE: "Jungle",
    MIDDLE: "Mid",
    BOTTOM: "Bot",
    UTILITY: "Support",
};

const STATS = [
    { key: "attackDamage", icon: "ad", label: "Attack damage", format: (v) => Math.round(v) },
    { key: "abilityPower", icon: "ap", label: "Ability power", format: (v) => Math.round(v) },
    { key: "armor", icon: "armor", label: "Armor", format: (v) => Math.round(v) },
    { key: "magicResist", icon: "mr", label: "Magic resist", format: (v) => Math.round(v) },
    { key: "attackSpeed", icon: "as", label: "Attack speed", format: (v) => v.toFixed(2) },
    { key: "tenacity", icon: "tenacity", label: "Tenacity", format: (v) => `${Math.round(v * 100)}%`, hideZero: true },
];

const ICONS = {
    sword: '<svg viewBox="0 0 16 16"><path d="M13.5 1.5l1 1-7.2 7.2 1.2 1.2-1 1-1.2-1.2-2.1 2.1.7.7-1 1-3-3 1-1 .7.7 2.1-2.1-1.2-1.2 1-1 1.2 1.2z"/></svg>',
};

const $ = (id) => document.getElementById(id);

const tipItems = new Map();
const tipWhy = new Map();
const tipHtml = new Map();

let lastState = null;
let lastRecommendation = null;

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

function seconds(value) {
    return value == null ? "30s+" : `${value.toFixed(1)}s`;
}

function setStatus(kind, text) {
    const el = $("status");
    el.className = `status status-${kind}`;
    el.textContent = text;
}

function itemAttrs(item, whyKey) {
    tipItems.set(item.riotId, item);
    return `data-item="${item.riotId}"${whyKey ? ` data-why="${whyKey}"` : ""}`;
}

function tipAttr(key, html) {
    tipHtml.set(key, html);
    return `data-tip="${esc(key)}"`;
}

function list(lines, cls) {
    return lines.length ? `<ul class="${cls}">${lines.map((l) => `<li>${esc(l)}</li>`).join("")}</ul>` : "";
}

function renderItemTip(item, why) {
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

function renderTip(target) {
    if (target.dataset.item) {
        const item = tipItems.get(Number(target.dataset.item));
        return item ? renderItemTip(item, tipWhy.get(target.dataset.why) ?? []) : "";
    }

    return tipHtml.get(target.dataset.tip) ?? "";
}

function showTip(target) {
    const tip = $("tip");
    const html = renderTip(target);
    if (!html) {
        hideTip();
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

for (const type of ["mouseover", "click"]) {
    document.addEventListener(type, (e) => {
        const target = e.target.closest("[data-item], [data-tip]");
        if (target) showTip(target); else hideTip();
    });
}

function renderObjectives(team, mirrored) {
    if (!team) {
        return "";
    }

    const o = team.objectives;
    const color = team.team === "Order" ? "blue" : "red";
    const drakes = [];

    for (const dragon of o.dragons) {
        for (let i = 0; i < dragon.count; i++) {
            drakes.push(`<img class="drake" src="img/dragons/${esc(dragon.type)}.png" alt="${esc(dragon.name)}" title="${esc(dragon.name)}" />`);
        }
    }

    for (let i = 0; i < o.elders; i++) {
        drakes.push('<img class="drake" src="img/dragons/Elder.png" alt="Elder Dragon" title="Elder Dragon" />');
    }

    const soul = o.soulType
        ? `<span class="soul soul-${esc(o.soulType)}"><img src="img/dragons/${esc(o.soulType)}.png" alt="" />${esc(o.soulName ?? o.soulType)}</span>`
        : "";

    const counts = [
        [`turret-${color}`, o.turrets, "Turrets destroyed"],
        [`inhib-${color}`, o.inhibitors, "Inhibitors destroyed"],
        ["herald", o.heralds, "Rift Heralds"],
        ["baron", o.barons, "Barons"],
    ].map(([icon, count, title]) => {
        const img = `<img src="img/objectives/${icon}.png" alt="${title}" />`;
        const value = `<b>${count}</b>`;
        return `<span class="objective${count ? "" : " objective-none"}" title="${title}">${mirrored ? value + img : img + value}</span>`;
    });

    const dragons = drakes.length ? `<span class="drakes">${(mirrored ? drakes.reverse() : drakes).join("")}</span>` : "";
    const parts = [counts.join(""), soul, dragons];

    return (mirrored ? parts.reverse() : parts).join("");
}

function renderItems(items, extraSlot) {
    const regular = new Array(ITEM_SLOTS).fill(null);
    let extra = null;
    let trinket = null;

    for (const item of items) {
        if (item.slot === TRINKET_SLOT && !trinket) {
            trinket = item;
        } else if (item.slot >= 0 && item.slot < ITEM_SLOTS && !regular[item.slot]) {
            regular[item.slot] = item;
        } else if (extraSlot && !extra && item.slot > TRINKET_SLOT) {
            extra = item;
        } else if (regular.includes(null)) {
            regular[regular.indexOf(null)] = item;
        } else if (extraSlot && !extra) {
            extra = item;
        }
    }

    const cell = (item, cls) => item
        ? `<div class="${cls}" ${itemAttrs(item)}>
               <img src="${esc(item.icon)}" alt="${esc(item.name)}" loading="lazy" />
               ${item.count > 1 ? `<span class="count">${item.count}</span>` : ""}
           </div>`
        : `<div class="${cls} slot-empty"></div>`;

    const cells = regular.map((item) => cell(item, "slot"));
    if (extraSlot) {
        cells.push(cell(extra, "slot slot-extra"));
    }
    cells.push(cell(trinket, "slot slot-trinket"));

    return `<div class="items">${cells.join("")}</div>`;
}

function renderIncome(player) {
    if (player.goldPerMinute == null) {
        return '<div class="gpm"></div>';
    }

    const source = player.isActivePlayer ? "from your exact gold" : "from item value, blended with the lobby's trend";
    return `<div class="gpm" title="Forecast gold per minute, ${source}"><span class="coin"></span>${Math.round(player.goldPerMinute)}<small>/m</small></div>`;
}

function renderStats(player) {
    const chips = STATS
        .map((s) => s.hideZero && !player.stats[s.key]
            ? '<span class="stat"></span>'
            : `<span class="stat" title="${s.label}"><img src="img/stats/${s.icon}.png" alt="${s.label}" />${s.format(player.stats[s.key])}</span>`)
        .join("");

    const title = player.statsAreExact ? "Your stats, straight from the game" : "Estimated from level, items and dragons";
    return `<div class="stats${player.statsAreExact ? " stats-exact" : ""}" title="${title}">${chips}</div>`;
}

function renderHealth(player, matchup) {
    const max = player.stats.health;
    const current = player.currentHealth ?? max;
    const fill = max > 0 ? Math.max(0, Math.min(100, (current / max) * 100)) : 0;
    const label = player.currentHealth != null ? `${Math.round(current)} / ${Math.round(max)}` : `${Math.round(max)}`;

    const cast = matchup?.casts?.[0];
    const tick = cast && max > 0
        ? `<span class="hp-tick" style="left:${Math.min(100, (cast.castAtHealth / max) * 100)}%"></span>`
        : "";

    return `
        <div class="hp">
            <img class="hp-icon" src="img/stats/health.png" alt="Health" />
            <div class="hp-bar">
                <span class="hp-fill" style="width:${fill}%"></span>
                ${tick}
                <span class="hp-label">${label}</span>
            </div>
        </div>`;
}

function killTime(value) {
    const window = lastRecommendation?.model?.teamfightSeconds;
    return value == null && window ? `${window}s+` : seconds(value);
}

function renderVersus(player, matchup, after, nextName) {
    if (!matchup?.modelled) {
        return "";
    }

    const cast = matchup.casts?.[0];
    const lines = [
        `<div class="tip-name">You vs ${esc(player.champion)}</div>`,
        `<div>Time to kill with your items: <b>${killTime(matchup.timeToKill)}</b> (${Math.round(matchup.dps)} DPS over a teamfight)</div>`,
    ];

    if (after && nextName) {
        lines.push(`<div>After ${esc(nextName)}: <b>${seconds(after.ttkAfter)}</b> (${Math.round(after.dpsAfter)} DPS)</div>`);
    }

    if (cast) {
        lines.push(`<div class="tip-label">When to cast ${esc(cast.ability)}</div>`);
        lines.push(`<div>At <b>${Math.round(cast.castAtHealth)}</b> HP or lower; it kills from ${Math.round(cast.killingHealth)}</div>`);
        lines.push(list(cast.additions, "tip-effects"));
    }

    const afterText = after && nextName ? `<span class="vs-after">→ ${seconds(after.ttkAfter)}</span>` : "";

    return `
        <span class="vs" ${tipAttr(`vs-${player.champion}`, lines.join(""))}>
            ${ICONS.sword}<b>${killTime(matchup.timeToKill)}</b>${afterText}
        </span>`;
}

function renderPlayer(player, context) {
    const classes = ["player"];
    if (player.isActivePlayer) classes.push("player-you");
    if (player.isDead) classes.push("player-dead");

    const matchup = context.matchups.get(player.champion);
    const after = context.after.get(player.champion);

    return `
        <li class="${classes.join(" ")}">
            <div class="portrait">
                <img src="${esc(player.championIcon)}" alt="${esc(player.champion)}" />
                <span class="level">${player.level}</span>
                ${player.isDead ? '<span class="dead-tag">Dead</span>' : ""}
            </div>
            <div class="who">
                <span class="champion">${esc(player.champion)}</span>
                <span class="role">${esc(POSITIONS[player.position] ?? player.position)}</span>
            </div>
            <div class="kda"><span class="k">${player.kills}</span><i>/</i><span class="d">${player.deaths}</span><i>/</i><span class="a">${player.assists}</span></div>
            ${renderIncome(player)}
            ${renderVersus(player, matchup, after, context.nextName)}
            ${renderItems(player.items, player.position === "BOTTOM")}
            ${renderHealth(player, matchup)}
            ${renderStats(player)}
        </li>`;
}

function renderTeam(team, element, context) {
    if (!team) {
        element.innerHTML = "";
        return;
    }

    const title = team.team === "Order" ? "Blue side" : "Red side";
    const tag = team.team === lastState?.activeTeam ? '<span class="team-tag">Your team</span>' : "";

    element.innerHTML = `
        <div class="team-head">
            <span class="team-title">${title}</span>${tag}
            <span class="team-kda">${team.kills} / ${team.deaths} / ${team.assists}</span>
        </div>
        <ul class="players">${team.players.map((p) => renderPlayer(p, context)).join("")}</ul>`;
}

function boardContext() {
    const model = new Map((lastRecommendation?.matchups ?? []).map((m) => [m.champion, m]));
    const matchups = new Map((lastState?.matchups ?? []).map((m) => {
        const fight = model.get(m.champion);
        return [m.champion, fight ? { ...m, timeToKill: fight.timeToKill, dps: fight.dps, modelled: true } : m];
    }));
    const next = lastRecommendation?.buildPath?.find((s) => s.status === "Next");
    const after = new Map((next?.impact?.perEnemy ?? []).map((e) => [e.champion, e]));

    return { matchups, after, nextName: next?.item?.name };
}

function renderBoard() {
    const state = lastState;
    $("patch").textContent = state?.patch ? `Patch ${state.patch}` : "";

    if (!state || state.phase !== "InGame") {
        $("game").hidden = true;
        $("waiting").hidden = false;
        return;
    }

    $("waiting").hidden = true;
    $("game").hidden = false;

    const order = state.teams.find((t) => t.team === "Order");
    const chaos = state.teams.find((t) => t.team === "Chaos");
    const context = boardContext();

    $("clock").textContent = clock(state.gameTime);
    $("order-kills").textContent = order?.kills ?? 0;
    $("chaos-kills").textContent = chaos?.kills ?? 0;
    $("order-objectives").innerHTML = renderObjectives(order, false);
    $("chaos-objectives").innerHTML = renderObjectives(chaos, true);

    renderTeam(order, $("team-order"), context);
    renderTeam(chaos, $("team-chaos"), context);

    const unknown = [
        ...state.unknownChampions.map((c) => `champion "${c}"`),
        ...state.unknownItemIds.map((id) => `item ${id}`),
    ];

    $("unknown").hidden = unknown.length === 0;
    $("unknown").textContent = unknown.length ? `Not in our data: ${unknown.join(", ")}` : "";
}

function eta(step, gameTime) {
    if (step.status === "Owned") {
        return "Built";
    }

    if (step.etaSeconds == null) {
        return "";
    }

    return step.etaSeconds <= gameTime + 1 ? "Now" : `~${clock(step.etaSeconds)}`;
}

function renderRecall(buyNow) {
    const icons = buyNow.items.map((item) => `
        <div class="slot slot-md" ${itemAttrs(item)}>
            <img src="${esc(item.icon)}" alt="${esc(item.name)}" loading="lazy" />
        </div>`).join("");

    const detail = buyNow.items.length ? `<span class="muted">${buyNow.cost}g · ${Math.floor(buyNow.goldLeft)} left</span>` : "";
    const why = buyNow.why?.[0] ? `<div class="muted small">${esc(buyNow.why[0])}</div>` : "";

    return `
        <div class="block">
            <div class="label">On recall</div>
            ${icons ? `<div class="recall-items">${icons}</div>` : ""}
            <div class="recall-summary">${esc(buyNow.summary)} ${detail}</div>
            ${why}
        </div>`;
}

function renderPath(rec, gameTime) {
    const steps = rec.buildPath.map((step, i) => {
        const key = `step-${i}`;
        tipWhy.set(key, step.status === "Owned" ? [] : step.why);

        return `
            <li class="step step-${step.status.toLowerCase()}" ${itemAttrs(step.item, key)}>
                <div class="slot slot-md"><img src="${esc(step.item.icon)}" alt="${esc(step.item.name)}" loading="lazy" /></div>
                <span class="step-eta">${eta(step, gameTime)}</span>
                ${step.sells ? `<span class="step-sells" title="Sells ${esc(step.sells)} to make room">sells ${esc(step.sells)}</span>` : ""}
            </li>`;
    }).join("");

    return `
        <div class="block">
            <div class="label">Build path</div>
            <ol class="steps">${steps}</ol>
        </div>`;
}

function renderWhy(rec) {
    const next = rec.buildPath.find((s) => s.status === "Next");
    if (!next) {
        return "";
    }

    return `
        <div class="block">
            <div class="label">Why ${esc(next.item.name)} next</div>
            ${list(next.why.slice(0, WHY_LINES), "why")}
        </div>`;
}

function renderNeeds(needs, gameTime) {
    return needs.map((n) => {
        const options = n.options.map((o) => `<span class="slot slot-xs" ${itemAttrs(o)}><img src="${esc(o.icon)}" alt="${esc(o.name)}" loading="lazy" /></span>`).join("");
        const status = n.coveredBy
            ? `<span class="need-state">${esc(n.coveredBy)}${n.coveredAtSeconds != null && n.coveredAtSeconds > gameTime + 1 ? ` ~${clock(n.coveredAtSeconds)}` : ""}</span>`
            : `<span class="need-state">not in build</span>${options}`;

        return `<span class="need ${n.coveredBy ? "need-ok" : "need-missing"}" ${tipAttr(`need-${n.need}`, `<div class="tip-name">${esc(n.need)}</div><div>${esc(n.detail)}</div>`)}><b>${esc(n.need)}</b>${status}</span>`;
    }).join("");
}

function pct(value) {
    return `${Math.round(value * 100)}%`;
}

function renderForm(rec) {
    const f = rec.form;
    if (!f) {
        return "";
    }

    const cards = f.options.map((o) => {
        const picked = o.form === f.recommended;
        const burst = o.burstTarget ? `<div>First 3s take <b>${pct(o.burstOnTarget ?? 0)}</b> of ${esc(o.burstTarget)}'s health, kill in <b>${killTime(o.burstKillSeconds)}</b></div>` : "";
        const heal = o.healingPerSecond >= 1 ? `<div>Heals <b>${Math.round(o.healingPerSecond)}</b>/s in fights</div>` : "";
        return `
            <div class="form-card${picked ? " form-picked" : ""}">
                <div class="form-name">${esc(o.label)}${picked ? '<span class="form-tag">Recommended</span>' : ""}</div>
                <div><b>${Math.round(o.dps)}</b> DPS, <b>${o.timeAlive.toFixed(1)}s</b> alive</div>
                <div>Damage over a fight: <b>${Math.round(o.fightValue)}</b></div>
                ${burst}${heal}
            </div>`;
    }).join("");

    return `
        <div class="form">
            <div class="model-head">
                <span class="label">Form${f.detected ? ` · you are ${esc(f.detected)}` : ""}</span>
                <span class="muted small">${esc(f.chargeHint)}</span>
            </div>
            <div class="form-cards">${cards}</div>
            ${list(f.why, "why")}
        </div>`;
}

function renderModel(rec) {
    const m = rec.model;
    if (!m) {
        return "";
    }

    const weights = [`damage ${m.damageWeight}`, `survival ${m.survivalWeight}`];
    if (m.clearWeight > 0.005) {
        weights.push(`clear ${m.clearWeight.toFixed(2)}`);
    }

    const you = [
        `<span><b>${Math.round(m.dps)}</b> DPS</span>`,
        m.survivalAbility
            ? `<span><b>${m.timeAliveWithoutAbility.toFixed(1)}s</b> alive under focus, <b>${m.timeAlive.toFixed(1)}s</b> with ${esc(m.survivalAbility)} (${m.teamfightSeconds}s fights)</span>`
            : `<span><b>${m.timeAlive.toFixed(1)}s</b> alive in a ${m.teamfightSeconds}s fight</span>`,
        `<span><b>${Math.round(m.incomingDps)}</b> damage/s on you (${pct(m.physicalShare)} physical, ${pct(m.magicShare)} magic, ${pct(m.trueShare)} true)</span>`,
        `<span>burst <b>${Math.round(m.incomingBurst)}</b> of ${Math.round(m.healthPool)} health</span>`,
    ];
    if (m.clearSeconds != null) {
        you.push(`<span>clear <b>${Math.round(m.clearSeconds)}s</b> of fighting</span>`);
    }

    const rows = m.enemies.map((e) => {
        const items = e.newItems.map((i) => `<span class="slot slot-xs" ${itemAttrs(i)}><img src="${esc(i.icon)}" alt="${esc(i.name)}" loading="lazy" /></span>`).join("");
        const notes = e.notes.length ? ` ${tipAttr(`fc-${e.champion}`, `<div class="tip-name">${esc(e.champion)}</div>${list(e.notes, "tip-effects")}`)}` : "";
        return `
            <tr${notes}>
                <td class="fc-champ"><img src="${esc(e.icon)}" alt="" />${esc(e.champion)} <span class="muted small">${esc(e.archetype)}</span></td>
                <td>${e.levelNow}${e.level > e.levelNow ? ` → <b>${e.level}</b>` : ""}</td>
                <td class="fc-items">${items || '<span class="muted">—</span>'}</td>
                <td>${Math.round(e.health)}</td>
                <td>${Math.round(e.armor)} / ${Math.round(e.magicResist)}</td>
                <td>${e.healPerSecond >= 1 ? Math.round(e.healPerSecond) + "/s" : "—"}</td>
                <td>${e.shield >= 1 ? Math.round(e.shield) : "—"}</td>
                <td>${Math.round(e.dpsOnYou)} <span class="muted small">${pct(e.focus)}</span></td>
                <td>${seconds(e.timeToKill)}</td>
                <td>${pct(e.threat)}</td>
            </tr>`;
    }).join("");

    const assumptions = rec.assumptions?.length
        ? `<details class="assumptions"><summary>Assumptions</summary>${list(rec.assumptions, "why")}</details>`
        : "";

    return `
        <div class="model">
            <div class="model-head">
                <span class="label">Forecast at ~${clock(m.at)}, when your next item lands</span>
                <span class="muted small">weights: ${weights.join(" · ")} · ${esc(m.stage)} search (${m.stageIndex}/${m.stageCount}): ${m.evaluations} builds scored in ${Math.round(m.planMilliseconds)} ms${m.timedOut ? " (time budget hit)" : ""}${m.refining ? ' · <span class="refining">thinking deeper in the background…</span>' : ""}</span>
                <span class="muted small">game state steady for ${Math.round(m.stableSeconds)}s · trends from ${m.trendSamples} reading${m.trendSamples === 1 ? "" : "s"}${m.trendConfidence < 1 ? ` · still leaning on the average game (${pct(m.trendConfidence)} observed)` : " · fully from this game"}</span>
            </div>
            <div class="model-you small">${you.join("")}</div>
            <table class="forecast">
                <thead><tr><th>Enemy</th><th>Level</th><th>New items</th><th>HP</th><th>Armor / MR</th><th>Heals</th><th>Shields</th><th>DPS on you</th><th>You kill in</th><th>Threat</th></tr></thead>
                <tbody>${rows}</tbody>
            </table>
            ${assumptions}
        </div>`;
}

let horizonSent = null;

async function setHorizon(value) {
    if (horizonSent === value) {
        return;
    }

    horizonSent = value;

    try {
        const response = await fetch("/api/preferences", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ coreItems: value }),
        });

        if (response.ok) {
            $("horizon-label").textContent = (await response.json()).label;
        }
    } catch {
        $("horizon-label").textContent = "not saved";
    }
}

function wireHorizon() {
    const slider = $("horizon");

    fetch("/api/preferences")
        .then(response => response.json())
        .then(preferences => {
            slider.value = preferences.coreItems;
            horizonSent = preferences.coreItems;
            $("horizon-label").textContent = preferences.label;
        })
        .catch(() => { });

    slider.addEventListener("input", () => {
        const items = Number(slider.value);
        $("horizon-label").textContent = `${items} item${items === 1 ? "" : "s"} + shoes`;
    });
    slider.addEventListener("change", () => setHorizon(Number(slider.value)));
}

function renderCore(m) {
    const done = Math.min(m.builtItems, m.coreItems);
    const complete = m.builtItems >= m.coreItems;
    const text = complete
        ? `Core done: ${m.coreItems} + shoes · now buying the best item each back`
        : `Core ${done} of ${m.coreItems} built`;

    return `<span class="core-state${complete ? " core-done" : ""}">${esc(text)}</span>`;
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
    const income = rec?.goldPerMinute != null ? `<span>Income <b>${Math.round(rec.goldPerMinute)}/min</b></span>` : "";

    const head = `
        <div class="build-head">
            <span class="build-title">Your build${rec?.isSample ? '<span class="sample">Sample</span>' : ""}</span>
            ${rec?.model ? renderCore(rec.model) : ""}
            <span class="purse">
                <span>Gold <b>${Math.floor(state.currentGold)}</b></span>
                <span>Earned <b>${gold(state.goldEarned)}</b></span>
                ${income}
            </span>
        </div>`;

    if (!rec) {
        el.innerHTML = `${head}<p class="muted small build-empty">No build recommendations yet.</p>`;
        return;
    }

    if (rec.calculating) {
        el.innerHTML = `${head}
            <p class="muted small build-empty build-calculating">
                <span class="waiting-ring waiting-ring-small"></span>
                Calculating a build…
            </p>`;
        return;
    }

    const needs = renderNeeds(rec.teamNeeds, state.gameTime);

    el.innerHTML = `
        ${head}
        <div class="build-grid">
            ${renderRecall(rec.buyNow)}
            ${renderPath(rec, state.gameTime)}
            ${renderWhy(rec)}
        </div>
        <div class="build-foot">
            ${needs ? `<div class="needs"><span class="label">Enemy team calls for</span>${needs}</div>` : "<div></div>"}
        </div>
        ${renderForm(rec)}
        ${renderModel(rec)}`;
}

function renderAll() {
    renderBoard();
    renderBuild();
}

function onState(state) {
    lastState = state;
    renderAll();
}

function onRecommendation(recommendation) {
    lastRecommendation = recommendation;
    renderAll();
}

async function loadInitial() {
    try {
        const response = await fetch("/api/state");
        if (response.ok) {
            onState(await response.json());
        }

        const recommendation = await fetch("/api/recommendation");
        const text = recommendation.ok ? await recommendation.text() : "";
        onRecommendation(text ? JSON.parse(text) : null);
    } catch {
    }
}

async function connect() {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hub")
        .withAutomaticReconnect()
        .build();

    connection.on("GameState", onState);
    connection.on("Recommendation", onRecommendation);
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

wireHorizon();
loadInitial();
connect();
