workaround = {};

// C# Console.WriteLine does not always work
workaround.writeline = function (message) {
    console.log(message);
};

nexus = {};
nexus.util = {};

nexus.util.addMouseUpEvent = function (dotNetHelper) {

    window.addEventListener("mouseup", e => dotNetHelper.invokeMethodAsync("OnMouseUp"), {
        once: true
    });
}

nexus.util.addClickEvent = function (dotNetHelper) {

    window.addEventListener("click", e => dotNetHelper.invokeMethodAsync("OnClick"), {
        once: true
    });
}

nexus.util.saveSetting = function (key, value) {
    if (window.localStorage)
        localStorage.setItem(key, JSON.stringify(value));
}

nexus.util.loadSetting = function (key) {

    if (window.localStorage)
        return JSON.parse(localStorage.getItem(key));

    else
        return null
}