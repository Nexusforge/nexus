var nexus = {};
nexus.chart = {};
nexus.chart.charts = {};

nexus.chart.resize = function (chartId, elementId, left, top, right, bottom) {

    let element = document
        .getElementById(`${elementId}_${chartId}`);

    element.style.left = `${left * 100}%`;
    element.style.top = `${top * 100}%`;
    element.style.width = `${(right - left) * 100}%`;
    element.style.height = `${(bottom - top) * 100}%`;
};

nexus.chart.setTextContent = function (chartId, elementId, text) {

    let element = document.getElementById(`${elementId}_${chartId}`);
    element.textContent = text;
};

nexus.chart.translate = function (chartId, elementId, left, top) {

    let element = document.getElementById(`${elementId}_${chartId}`);
    element.style.removeProperty("display")
    element.style.left = `${left * 100}%`;
    element.style.top = `${top * 100}%`;
};

nexus.chart.hide = function (chartId, elementId) {

    let element = document.getElementById(`${elementId}_${chartId}`);
    element.style.display = "none"
};

nexus.chart.toRelative = function (chartId, clientX, clientY) {

    let overlay = document
        .getElementById(`overlay_${chartId}`);

    let rect = overlay
        .getBoundingClientRect();

    let x = (clientX - rect.left) / rect.width;
    let y = (clientY - rect.top) / rect.height;

    return {
        "x": x,
        "y": y
    };
}

nexus.chart.addMouseUpEvent = function (dotNetHelper) {

    window.addEventListener("mouseup", e => dotNetHelper.invokeMethodAsync("OnMouseUp"), {
        once: true
    });
}