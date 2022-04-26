workaround = {};

// C# Console.WriteLine does not always work
workaround.writeline = function (message) {
    console.log(message);
};

workaround.addMouseUpEvent = function (dotNetHelper) {

    window.addEventListener("mouseup", e => dotNetHelper.invokeMethodAsync("OnMouseUp"), {
        once: true
    });
}

workaround.addClickEvent = function (dotNetHelper) {

    window.addEventListener("click", e => dotNetHelper.invokeMethodAsync("OnClick"), {
        once: true
    });
}