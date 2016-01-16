var readline = require("readline");

var width = 300;
var height = 300;

var scale = width / 25;

var drawFreq = 1;

var grid = createStartGrid();

function alive(ix, iy, grid) {
	if(ix < 0 || iy < 0 || ix >= width || iy >= height) {
		return false;
	}
	return grid[ix][iy];
}

function createBlankGrid() {
	var grid = [];
	for(var ix = 0; ix < width; ix++) {
		var arr = [];
		for(var iy = 0; iy < height; iy++) {
			arr.push(false);
		}
		grid.push(arr);
	}
	return grid;
}

function createStartGrid() {
	var grid = createBlankGrid();

var seed = 
  "....O.O......OO.............................\n"
+ ".....OO......OO.............................\n"
+ ".....O......................................\n"
+ "............................................\n"
+ "............................................\n"
+ "............................................\n"
+ "............................................\n"
+ "............................................\n"
+ "............................................\n"
+ "........................................O...\n"
+ ".......................................O.O..\n"
+ ".......................................O.O..\n"
+ "....................OO................OO.OO.\n"
+ "....................OO......................\n"
+ "......................................OO.OO.\n"
+ "..OO..................................OO.O..\n"
+ ".O.O.......................................O\n"
+ ".O........................................OO\n"
+ "OO..........................................\n"
+ "............................................\n"
+ "..................................OO........\n"
+ "..................................OO....OO..\n"
+ "...........OO...........................O.O.\n"
+ "..........O.O.............................O.\n"
+ "..........O...............................OO\n"
+ ".........OO.......................OO........\n"
+ "..................................OO........\n"
+ "............................................\n"
+ "............................................\n"
+ ".............................O..............\n"
+ "............................O.O.............\n"
+ ".............................O..............\n";

	seed = 
		".O.....\n"
	+	"...O...\n"
	+	"OO..OOO\n";

	var iy = 0;
	seed.split("\n").forEach(function(line){
		var ix = 0;
		line.split("").forEach(function(ch){
			grid[ix++][iy] = (ch === "O");
		});
		iy++;
	});

	grid = createBlankGrid();

	grid[1][33] = true;
	grid[2][33] = true;
	grid[3][33] = true;

	grid[4][11] = true;
	grid[4][12] = true;
	grid[4][13] = true;

	grid[10][5] = true;
	grid[11][5] = true;
	grid[12][5] = true;

	grid[21][6] = true;
	grid[22][6] = true;
	grid[23][6] = true;
	grid[23][5] = true;
	grid[23][4] = true;
	grid[22][3] = true;

	grid[78][54] = true;
	grid[78][55] = true;
	grid[78][56] = true;


	/*
	var size = Math.min(width, height);
	var border = 14;
	for(var i = border; i <= size - border; i++) {
		grid[i][i] = true;
		grid[size - i][i] = true;
		grid[i][size - i] = true;
		grid[size - i][i] = true;
	}
	var boxSize = 20;
	var boxBorder = 3;
	for(var i = 0; i < boxSize; i++) {
		for(var w = 0; w < boxBorder; w++) {
			grid[size / 2 - i + boxSize / 2][size / 2 - boxSize / 2 + w] = true;
			grid[size / 2 - i + boxSize / 2][size / 2 + boxSize / 2 - w] = true;
			grid[size / 2 - boxSize / 2 + w][size / 2 - i + boxSize / 2] = true;
			grid[size / 2 + boxSize / 2 - w][size / 2 - i + boxSize / 2] = true;
		}
	}

	var middleSize = boxSize - boxBorder * 2;
	var middleOffset = ~~(middleSize / 2);
	for(var ox = 0; ox <= middleSize ; ox++) {
		for(var oy = 0; oy <= middleSize; oy++) {
			grid[size / 2 - middleOffset + ox][size / 2 - middleOffset + oy] = false;
		}
	}
	*/

	var offsetX = width / 2;
	var offsetY = height / 2;

	var newGrid = createBlankGrid();
	for(var iy = 0; iy < height - offsetY; iy ++) {
		for(var ix = 0; ix < width - offsetX; ix ++) {
			newGrid[ix + offsetX][iy + offsetY] = grid[ix][iy];
		}
	}
	grid = newGrid;

	/*
	grid[1 + offsetX][1 + offsetY] = true;
	grid[1 + offsetX][2 + offsetY] = true;
	grid[1 + offsetX][3 + offsetY] = true;

	grid[3 + offsetX][2 + offsetY] = true;
	grid[4 + offsetX][2 + offsetY] = true;
	grid[5 + offsetX][2 + offsetY] = true;
	*/
	return grid;
}

var pid = process.pid.toString();

var iterations = 0;
function runIteration() {
	if(iterations % drawFreq === 0) {
		var newScreen = "";
		for(var iy = 0; iy < height; iy += scale) {
			var arr = [];
			for(var ix = 0; ix < width; ix += scale) {
				var count = 0;
				for(var ox = 0; ox < scale; ox++) {
					for(var oy = 0; oy < scale; oy++) {
						if(grid[ix + ox][iy + oy]) {
							count++;
						}
					}
				}
				
				newScreen += count > 0 ? count : " ";
			}
			newScreen += "\n";
		}
		newScreen += iterations + "\n";
		newScreen += pid + "\n";

		readline.cursorTo(process.stdout, 0, 0);
		readline.clearScreenDown(process.stdout);
		process.stdout.write(newScreen);
	}
	iterations++;

	
	var newGrid = createBlankGrid();
	for(var ix = 0; ix < width; ix++) {
		for(var iy = 0; iy < height; iy++) {
			var count = 0;
			for(var ox = -1; ox <= 1; ox++) {
				for(var oy = -1; oy <= 1; oy++) {
					if(ox === 0 && oy === 0) continue;
					if(alive(ix + ox, iy + oy, grid)) {
						count++;
					}
				}
			}
			if (!alive(ix, iy, grid)) {
				if(count === 3) {
					newGrid[ix][iy] = true;
				}
			}
			if (alive(ix, iy, grid)) {
				newGrid[ix][iy] = (count === 2 || count === 3);
			}
		}
	}
	grid = newGrid;
}

console.log(process.pid);

//startDebugger();

function startDebugger() {
	var net = require("net");
	var client = new net.Socket();
	//process.debugPort = 5859;

	//var startDebugProcess = require("child_process").spawn("kill", ["-s", "USR1", process.pid.toString()]); 

	var continueRequest = 'Content-Length: 47\\r\\n\\r\\n{\\"seq\\":1,\\"type\\":\\"request\\",\\"command\\":\\"continue\\"}';

	console.log(process.listeners("SIGUSR1"));

	var startDebugProcess = require("child_process").spawn("node", 
	[
		"-e", 
		" process._debugProcess(" + process.pid + ");" +
		" var client = require('net').Socket();" +
		" client.on('data', function(data){ var text = data.toString(); console.log(text); });" +
		" client.on('close', function(){ console.log('closed'); });" +
		" client.on('error', function(err){ console.log('error ' + err.toString()); });" +
		//Wait for the process to start listening, as it takes some time... wtf...
		" setTimeout(function(){ client.connect(5858, 'localhost', function(){ " +
			" setTimeout(function(){ client.write('"+continueRequest+"'); }, 500); " +
		" }); }, 1000)"
	]);


	var log = function(text) {
		console.log("proc " + text);
		boot();
	}
	startDebugProcess.stdout.on("data", log);
	startDebugProcess.stderr.on("data", log);
	startDebugProcess.on("close", function() {
		console.log("child closed");
	});
	startDebugProcess.on("error", function(err) {
		log(err);
	});
}


boot();

var port = 5858;
setInterval(function() {
	//port++;
	console.log("listening on port " + port);
	process.debugPort = port;
}, 10000);


//*/

//setInterval(runIteration, 0);

function boot() {
	console.log("boot");
	setInterval(function() {
		console.log("tick " + process.pid);
	}, 1000);
	return;
	while(true) {
		runIteration();
		if(iterations > 13000) {
			iterations = 0;
			grid = createStartGrid();
		}
	}
}

//process.on('SIGUSR1', function(){   console.trace();   });
/*
setTimeout(function() {
	while(true) {
		runIteration();
		if(iterations > 13000) {
			iterations = 0;
			grid = createStartGrid();
		}
	}
}, 1000);
*/
/*
setInterval(function(){
	console.log(iterations++);
}, 1000);
*/


//setInterval(runIteration, 10);
/*

*/

/*
while(true) {
	runIteration();
}
*/

 