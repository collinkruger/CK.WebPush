(function () {
    "use strict";
    const scheme = document.location.protocol === "https:" ? "wss" : "ws";
    const wsURL = scheme + "://localhost:{{port}}/ws";

    function init(url) {
        var socket = new WebSocket(url);
        socket.onopen = function (e) {
            console.log("Connected to CK.WebPush");
        };
        socket.onclose = function (e) {
            console.log("Disconnected from CK.WebPush");
        };
        socket.onerror = function (e) {
            alert("CK.WebPush Error. See Console");
            console.error("CK.WebPush Error", e, this);
        };
        socket.onmessage = function (event) {
            let data = event.data;
            if (typeof data === "string") {
                let command = data.split("|")[0];
                let type = data.split("|")[1];

                if (command === "reload") {
                    window.location.reload("true");
                    return;
                }
            }

            // TODO: Update single actual link
            var tags = document.getElementsByTagName("link");
            for (var i = 0; i < tags.length; i++) {
                var href = tags[i].href;
                var regex = /_=.*/g;
                var tail = "_=" + Math.random();
                tags[i].href = regex.test(href) ? href.replace(regex, tail) : href + "?" + tail;
            }
        };

        //return socket;
    }
    
    // Initializing after a short delay because reasons
    setTimeout(init(wsURL), 500);
}());