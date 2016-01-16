const TTY = process.binding('tty_wrap').TTY;
console.log(process._getActiveHandles().length);
var tty = new TTY(1, true);
console.log(process._getActiveHandles().length);
setTimeout(function(){ console.log("done") }, 5000);