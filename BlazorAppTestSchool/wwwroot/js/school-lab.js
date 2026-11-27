window.schoolLab = {
    search() {
        return location.pathname + location.search;
    },
    back() {
        return new Promise((resolve) => {
            const before = location.href;
            const timer = setTimeout(() => resolve(false), 150);
            window.addEventListener("popstate", function onPop() {
                window.removeEventListener("popstate", onPop);
                clearTimeout(timer);
                resolve(location.href !== before);
            });
            history.back();
        });
    }
};
