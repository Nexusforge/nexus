workaround = {};

// C# Console.WriteLine does not always work
workaround.writeline = function (message) {
    console.log(message);
};