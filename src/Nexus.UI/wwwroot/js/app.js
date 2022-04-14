var nexus = {};

nexus.translateCrosshairs = function (x, y) {
    var crossHairsX = document.getElementById("crosshairs-x");
    var crossHairsY = document.getElementById("crosshairs-y");

    crossHairsX.style.removeProperty("display")
    crossHairsX.style.transform = `translate(${0}px,${y}px)`;

    crossHairsY.style.removeProperty("display")
    crossHairsY.style.transform = `translate(${x}px,${0}px)`;
};

nexus.hideCrosshairs = function () {
    var crossHairsX = document.getElementById("crosshairs-x");
    var crossHairsY = document.getElementById("crosshairs-y");

    crossHairsX.style.display = "none"
    crossHairsY.style.display = "none"
};