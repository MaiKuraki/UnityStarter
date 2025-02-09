const ProximaWebGLLib = {
    $channels: {},

    ProximaWebGLCreate: function(displayName, instanceId, productName, companyName, platform, productVersion, proximaVersion, connectionId, messageCb, closeCB) {
        const connId = UTF8ToString(connectionId);
        const instId = UTF8ToString(instanceId);
        const inChannel = new BroadcastChannel('proxima-' + connId + '-in');
        const outChannel = new BroadcastChannel('proxima-' + connId + '-out');

        const conIdUtf8Length = lengthBytesUTF8(connId) + 1;
        const connIdUtf8 = _malloc(conIdUtf8Length);
        stringToUTF8(connId, connIdUtf8, conIdUtf8Length);

        inChannel.onmessage = function(e) {
            if (e.data === 'close') {
                Module.dynCall_vi(closeCB, connIdUtf8);
                return;
            }

            const size = lengthBytesUTF8(e.data) + 1;
            const buffer = _malloc(size);
            stringToUTF8(e.data, buffer, size);

            try {
                Module.dynCall_vii(messageCb, connIdUtf8, buffer);
            } finally {
                _free(buffer);
            }
        };

        const channel = {
            out: outChannel,
            in: inChannel,
            connIdUtf8: connIdUtf8,
        };

        channels[connId] = channel;

        const connections = [{
            DisplayName: UTF8ToString(displayName),
            InstanceId: instId,
            ProductName: UTF8ToString(productName),
            CompanyName: UTF8ToString(companyName),
            Platform: UTF8ToString(platform),
            ProductVersion: UTF8ToString(productVersion),
            ProximaVersion: UTF8ToString(proximaVersion),
            ConnectionId: connId
        }];

        // Avoid hanging onto old connections in localStorage by clearing it out
        window.localStorage.setItem('proxima-connections', JSON.stringify(connections));

        window.addEventListener('pagehide', function() {
            if (channel.out) {
                const connections = window.localStorage.getItem('proxima-connections');
                if (connections && connections.includes(connId)) {
                    window.localStorage.setItem('proxima-connections', '[]');
                }

                channel.out.postMessage('close');
            }
        });
    },

    ProximaWebGLSelected: function(connectionId) {
        window.localStorage.setItem('proxima-connections', '[]');
    },

    ProximaWebGLOpenOnMouseUp: function(url) {
        const urlString = UTF8ToString(url);
        function openUrl() {
            window.open(urlString, '_blank');
            document.removeEventListener('mouseup', openUrl);
        }

        document.addEventListener('mouseup', openUrl);
    },

    ProximaWebGLDestroy: function(connectionId) {
        const connId = UTF8ToString(connectionId);

        const outChannel = channels[connId].out;
        if (outChannel) {
            outChannel.postMessage('close');
            outChannel.close();
        }

        const inChannel = channels[connId].in;
        if (inChannel) {
            inChannel.close();
        }

        const connIdUtf8 = channels[connId].connIdUtf8;
        if (connIdUtf8) {
            _free(connIdUtf8);
        }

        const connections = window.localStorage.getItem('proxima-connections');
        if (connections && connections.includes(connId)) {
            window.localStorage.setItem('proxima-connections', '[]');
        }

        delete channels[connId];
    },

    ProximaWebGLSend: function(connectionId, data) {
        const connId = UTF8ToString(connectionId);
        channels[connId].out.postMessage(UTF8ToString(data));
    }
}

autoAddDeps(ProximaWebGLLib, '$channels');
mergeInto(LibraryManager.library, ProximaWebGLLib);