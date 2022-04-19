var nexus = {};
nexus.chart = {};

nexus.chart.resize = function (left, top, right, bottom) {

    let rect = document
        .getElementById("chart")
        .getBoundingClientRect();

    let overlay = document
        .getElementById("overlay");

    let left_new = rect.width * left;
    let top_new = rect.height * top;
    let right_new = rect.width * right;
    let bottom_new = rect.height * bottom;

    overlay.style.left = `${left_new}px`;
    overlay.style.top = `${top_new}px`;
    overlay.style.width = `${right_new - left_new}px`;
    overlay.style.height = `${bottom_new - top_new}px`;
};

nexus.chart.translate = function (elementId, left, top) {

    let element = document.getElementById(elementId);
    element.style.removeProperty("display")
    element.style.left = `${left * 100}%`;
    element.style.top = `${top * 100}%`;
};

nexus.chart.hide = function (elementId) {

    let element = document.getElementById(elementId);
    element.style.display = "none"
};

nexus.chart.toRelative = function (clientX, clientY) {

    let overlay = document
        .getElementById("overlay");

    let rect = overlay
        .getBoundingClientRect();

    let x = (clientX - rect.left) / rect.width;
    let y = (clientY - rect.top) / rect.height;

    return {
        "x": x,
        "y": y
    };
}