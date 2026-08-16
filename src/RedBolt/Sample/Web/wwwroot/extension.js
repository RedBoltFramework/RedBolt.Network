// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

const connections = new Map();
const channelIndexes = new Map([
    ['uu', 0],
    ['ru', 1],
    ['uo', 2],
    ['ro', 3]
]);
let interop;

function encodeSignal(...values) {
    const encoder = new TextEncoder();
    const encoded = values.map(value => encoder.encode(value));
    const result = new Uint8Array(encoded.reduce((size, value) => size + value.length + 1, 0));
    let offset = 0;
    for (const value of encoded) {
        result.set(value, offset);
        offset += value.length + 1;
    }
    return result;
}

function decodeSignal(value) {
    const bytes = new Uint8Array(value);
    const decoder = new TextDecoder();
    const result = [];
    let begin = 0;
    for (let end = 0; end <= bytes.length; ++end) {
        if (end === bytes.length || bytes[end] === 0) {
            result.push(decoder.decode(bytes.subarray(begin, end)));
            begin = end + 1;
        }
    }
    return result;
}

function bytesToBase64(bytes) {
    let binary = '';
    const chunkSize = 0x8000;
    for (let offset = 0; offset < bytes.length; offset += chunkSize)
        binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
    return btoa(binary);
}

function base64ToBytes(value) {
    const binary = atob(value);
    const result = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; ++index)
        result[index] = binary.charCodeAt(index);
    return result;
}

function close(id, error = '') {
    const state = connections.get(id);
    if (!state || state.closed)
        return;

    state.closed = true;
    state.signalingComplete = true;
    connections.delete(id);
    for (const channel of state.channels) {
        if (!channel)
            continue;
        channel.onopen = null;
        channel.onclose = null;
        channel.onmessage = null;
        channel.close();
    }
    state.peer.ondatachannel = null;
    state.peer.onconnectionstatechange = null;
    state.peer.close();
    state.socket.onclose = null;
    state.socket.close();
    interop.Disconnected(id, error);
}

function initializeChannel(state, channel) {
    const index = channelIndexes.get(channel.label);
    if (index === undefined) {
        channel.close();
        return;
    }

    state.channels[index] = channel;
    channel.binaryType = 'arraybuffer';
    channel.onopen = () => {
        if (state.connected || !state.channels.every(item => item?.readyState === 'open'))
            return;
        state.connected = true;
        state.signalingComplete = true;
        state.socket.close();
        interop.Connected(state.id);
    };
    channel.onclose = () => close(
        state.id, state.connected ? '' : 'A WebRTC data channel closed before connecting.');
    channel.onmessage = async event => {
        const value = event.data instanceof Blob ? await event.data.arrayBuffer() : event.data;
        const bytes = value instanceof ArrayBuffer
            ? new Uint8Array(value)
            : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
        interop.Data(state.id, bytesToBase64(bytes));
    };
}

async function handleSignal(state, event) {
    const value = event.data instanceof Blob ? await event.data.arrayBuffer() : event.data;
    const [type, first = '', second = ''] = decodeSignal(value);
    if (type === 'offer' || type === 'answer') {
        await state.peer.setRemoteDescription({ type, sdp: first });
        if (type === 'offer') {
            const answer = await state.peer.createAnswer();
            await state.peer.setLocalDescription(answer);
            state.socket.send(encodeSignal('answer', state.peer.localDescription.sdp));
        }
    } else if (type === 'candidate') {
        await state.peer.addIceCandidate({ candidate: first, sdpMid: second || null });
    }
}

function connect(id, url, iceServers) {
    try {
        const peer = new RTCPeerConnection({
            iceServers: iceServers
                ? iceServers.split('\n').filter(value => value).map(urls => ({ urls }))
                : []
        });
        const socket = new WebSocket(url);
        socket.binaryType = 'arraybuffer';
        const state = {
            id,
            peer,
            socket,
            channels: new Array(4),
            signalQueue: Promise.resolve(),
            connected: false,
            signalingComplete: false,
            closed: false
        };
        connections.set(id, state);

        peer.ondatachannel = event => initializeChannel(state, event.channel);
        peer.onicecandidate = event => {
            if (event.candidate && socket.readyState === WebSocket.OPEN)
                socket.send(encodeSignal(
                    'candidate', event.candidate.candidate, event.candidate.sdpMid || ''));
        };
        peer.onconnectionstatechange = () => {
            if (peer.connectionState === 'failed' || peer.connectionState === 'closed')
                close(id, `WebRTC connection ${peer.connectionState}.`);
        };
        socket.onmessage = event => {
            state.signalQueue = state.signalQueue
                .then(() => handleSignal(state, event))
                .catch(error => close(id, String(error)));
        };
        socket.onclose = () => {
            if (!state.signalingComplete)
                close(id, 'The signaling WebSocket closed before WebRTC connected.');
        };
        socket.onerror = () => {
            if (!state.signalingComplete)
                close(id, 'The signaling WebSocket failed.');
        };
        return true;
    } catch (error) {
        console.error(error);
        return false;
    }
}

function send(id, packetType, payload) {
    const channel = connections.get(id)?.channels[packetType];
    if (channel?.readyState !== 'open')
        return false;
    channel.send(base64ToBytes(payload));
    return true;
}

export async function createModuleImports({ getAssemblyExports }) {
    const exports = await getAssemblyExports('RedBolt.Network.dll');
    interop = exports.RedBolt.Network.BrowserDataChannelInterop;
    return { network: { connect, send, close } };
}
