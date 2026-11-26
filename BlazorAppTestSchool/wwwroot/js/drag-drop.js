// Reusable pointer-based drag-and-drop for Blazor Server test questions.
// No external libraries: works with mouse, touch and pen via the Pointer Events API,
// and falls back to tap-to-pick / tap-to-place for accessibility and flaky touch drags.
// Markup contract: draggable chips carry [data-dnd-item="id"], drop targets carry [data-dnd-zone="id"].

// Used by the teacher's question builder: converts a click point into a percentage position
// relative to an image element, so hotspots stay correctly placed at any image size.
export function relativePercent(element, clientX, clientY) {
    const rect = element.getBoundingClientRect();
    const x = Math.min(100, Math.max(0, ((clientX - rect.left) / rect.width) * 100));
    const y = Math.min(100, Math.max(0, ((clientY - rect.top) / rect.height) * 100));
    return [x, y];
}

export function init(containerEl, dotNetRef) {
    let drag = null;
    let pickedId = null;

    function escapeId(value) {
        return window.CSS && CSS.escape ? CSS.escape(value) : value.replace(/[^a-zA-Z0-9_-]/g, '\\$&');
    }

    function findItem(id) {
        return containerEl.querySelector(`[data-dnd-item="${escapeId(id)}"]`);
    }

    function zoneAt(x, y) {
        const el = document.elementFromPoint(x, y);
        return el ? el.closest('[data-dnd-zone]') : null;
    }

    function clearActiveZones() {
        containerEl.querySelectorAll('.dnd-zone-active').forEach(zone => zone.classList.remove('dnd-zone-active'));
    }

    function setPicked(id) {
        if (pickedId) {
            findItem(pickedId)?.classList.remove('dnd-item-picked');
        }
        pickedId = id;
        if (id) {
            findItem(id)?.classList.add('dnd-item-picked');
        }
    }

    function ensureGhost() {
        if (drag.ghostEl) {
            return drag.ghostEl;
        }

        const ghost = drag.itemEl.cloneNode(true);
        ghost.classList.add('dnd-ghost');
        ghost.style.position = 'fixed';
        ghost.style.left = '0';
        ghost.style.top = '0';
        ghost.style.width = drag.width + 'px';
        ghost.style.margin = '0';
        ghost.style.pointerEvents = 'none';
        document.body.appendChild(ghost);
        drag.itemEl.classList.add('dnd-item-lifted');
        drag.ghostEl = ghost;
        return ghost;
    }

    function cleanupDrag() {
        drag.itemEl.removeEventListener('pointermove', onPointerMove);
        drag.itemEl.removeEventListener('pointerup', onPointerUp);
        drag.itemEl.removeEventListener('pointercancel', onPointerUp);
        drag.itemEl.classList.remove('dnd-item-lifted');
        drag.ghostEl?.remove();
        clearActiveZones();
    }

    function onPointerDown(event) {
        const item = event.target.closest('[data-dnd-item]');
        if (!item || item.hasAttribute('data-dnd-disabled') || !containerEl.contains(item)) {
            return;
        }

        const rect = item.getBoundingClientRect();
        drag = {
            itemId: item.dataset.dndItem,
            itemEl: item,
            startX: event.clientX,
            startY: event.clientY,
            offsetX: event.clientX - rect.left,
            offsetY: event.clientY - rect.top,
            width: rect.width,
            moved: false
        };
        item.setPointerCapture?.(event.pointerId);
        item.addEventListener('pointermove', onPointerMove);
        item.addEventListener('pointerup', onPointerUp);
        item.addEventListener('pointercancel', onPointerUp);
    }

    function onPointerMove(event) {
        if (!drag) {
            return;
        }

        const dx = event.clientX - drag.startX;
        const dy = event.clientY - drag.startY;
        if (!drag.moved && Math.hypot(dx, dy) < 6) {
            return;
        }

        drag.moved = true;
        event.preventDefault();
        const ghost = ensureGhost();
        ghost.style.transform = `translate(${event.clientX - drag.offsetX}px, ${event.clientY - drag.offsetY}px)`;
        clearActiveZones();
        zoneAt(event.clientX, event.clientY)?.classList.add('dnd-zone-active');
    }

    function onPointerUp(event) {
        if (!drag) {
            return;
        }

        const { itemId, moved } = drag;
        if (moved) {
            const zone = zoneAt(event.clientX, event.clientY);
            cleanupDrag();
            drag = null;
            setPicked(null);
            if (zone) {
                dotNetRef.invokeMethodAsync('OnDrop', itemId, zone.dataset.dndZone);
            }
        } else {
            cleanupDrag();
            drag = null;
            setPicked(pickedId === itemId ? null : itemId);
        }
    }

    function onContainerClick(event) {
        if (event.target.closest('[data-dnd-item]')) {
            // Clicks on a chip (picked up or already placed) are handled entirely by
            // onPointerUp's tap-to-pick/unpick logic, even when the chip sits inside a zone.
            return;
        }

        const zone = event.target.closest('[data-dnd-zone]');
        if (!zone || !pickedId) {
            return;
        }

        const itemId = pickedId;
        setPicked(null);
        dotNetRef.invokeMethodAsync('OnDrop', itemId, zone.dataset.dndZone);
    }

    containerEl.addEventListener('pointerdown', onPointerDown);
    containerEl.addEventListener('click', onContainerClick);

    return {
        dispose() {
            containerEl.removeEventListener('pointerdown', onPointerDown);
            containerEl.removeEventListener('click', onContainerClick);
        }
    };
}
