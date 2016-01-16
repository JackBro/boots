var net = require("net");

var child_process = require('child_process');

console.log(process.pid);
var nodePids = [];
child_process.exec("tasklist", function(err, stdout, stderr) {
	var list = stdout.toString();
	var processes = list.split("\n");
	processes.forEach(function(processLine) {
		var info = processLine.split(/\s+/);
		var name = info[0];
		var pid = +info[1];

		if (name === "node.exe") {
			if(pid !== process.pid && pid !== 4620) {
				nodePids.push(pid);
			}
		}
	});

	console.log(nodePids);
	boot(nodePids[0]);
});

//process.exit();

var client = new net.Socket();

var log = console.log.bind(console);

var callbackLists = {};

var seqNumber = 1;
function sendRequest(command, arguments, callback) {
	var seq = seqNumber++;
	var reqObj = {
		seq: seq,
		type: "request",
		command: command
	}
	if(arguments) {
		reqObj["arguments"] = arguments
	}
	var messageBody = JSON.stringify(reqObj);

	var message = "Content-Length: "
		+ messageBody.length + "\r\n"
		+ "\r\n"
		+ messageBody
	;

	if(callback) {
		callbackLists[seq] = callbackLists[seq] || [];
		callbackLists[seq].push(callback);
	}

	console.log("Send: \n" + message);
	client.write(message, log);

	return seq;
}

var opened = false;

function dataHandler(text) {
	if(!opened) {
		opened = true;
		onConnect();
	}

	var handled = false;

	try {
		var obj = JSON.parse(text);
		if("request_seq" in obj) {
			var seq = +obj.request_seq;
			console.log("Got seq " + seq);
			if(seq in callbackLists) {
				var callbacks = callbackLists[seq];
				delete callbackLists[seq];
				callbacks.forEach(function(callback){
					callback(obj.body);
				});
				handled = true;
			}
		}
	} catch(err) { }

	if(!handled) {
		var firstCount = 500;
		var lastCount = 500;
		if(text.length < firstCount * 2) {
			lastCount = firstCount = text.length / 2;
			if(text.length % 2 === 1) {
				firstCount++;
			}
		}

		console.log('Received: '
			+ text.length
			+ text.slice(0, firstCount) + "\t"
			+ text.slice(-lastCount));
	}
}

var contentLeft = 0;
var contentSoFar = "";
client.on('data', function(data) {
	var text = data.toString();
	var lenText = "Content-Length: ";
	//This is wrong... but... w/e
	if (text.slice(0, lenText.length) === lenText) {
		var end = text.indexOf("\r\n");
		var len = text.slice(lenText.length, end);
		contentLeft = +len;
		contentSoFar = "";
		text = text.slice(end + 4);
	}

	contentSoFar += text;

	contentLeft -= text.length;
	if(contentLeft <= 0) {
		dataHandler(contentSoFar);
	}
});
client.on('close', function() {
	console.log('Connection closed');
});

var args = process.argv.slice(2);
var port = +args[0] || 5858;

function boot(targetPID) {
	child_process.exec("node -e \"process._debugProcess(" + targetPID + ")\"", function(err, stdout, stderr) {
		/*
		setTimeout(function(){
			console.log("Connecting to " + port);
			client.connect(port, "localhost", function() {
				console.log('Connected');
			});
		}, 1000);
		*/
	});
}

function onConnect() {
	
}

process.stdin.on("data", function(chunk) {
	var text = chunk.toString().split(/[\r\n]/)[0];
	var command = text.split(" ")[0]
	var args = text.substring(text.indexOf(" ") + 1);
	switch(command) {
		//suspend
		//continue
		//listbreakpoints
		case "connect":
			client.connect(port, "localhost", function() {
				console.log('Connected');
			});
			break;
		case "scripts":
			sendRequest("scripts", null, function(resp) {
				resp.forEach(function(script) {
					if(script.name.indexOf("test") >= 0) {
						console.log(script);
					}
				});
			})
			break;
		case "setbreakpoint":
			sendRequest("setbreakpoint", {
				type: "scriptRegExp",
				target: ".*test\\.js",
				line: 13
			});
			break;
		case "evaluate":
			sendRequest("evaluate", {
				expression: "process.debugPort = 5859",
				global: true
			});
			break;
		case "stop":
			sendRequest("evaluate", {
				expression: "process._debugEnd()",
				global: true
			});
			break;
		case "nullptr":
			client.write("Content-Length: 0\r\n\r\n", log);
			break;
		case "doit":
			sendRequest("continue", null, function() { 
				sendRequest("evaluate", {
					expression: "setTimeout(function(){ process.debugPort = 5859; process._debugEnd(); }, 10000)",
					global: true
				}, function() {
					sendRequest("disconnect", null, function(resp) {
						client.destroy();
					});
				});
			});
			break;
		case "disconnect":
		case "close":
			sendRequest("disconnect", null, function(resp) {
				client.destroy();
			});
			break;
		default:
			sendRequest(command);
			break;
	}
});

/*
var client = require('v8-debugger').createClient({port: 5858});
client.pause();
console.log("here");
*/