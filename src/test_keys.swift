import MediaPlayer

let center = MPRemoteCommandCenter.shared()

center.playCommand.addTarget { event in
    print("PLAY")
    return .success
}
center.pauseCommand.addTarget { event in
    print("PAUSE")
    return .success
}
center.togglePlayPauseCommand.addTarget { event in
    print("TOGGLE")
    return .success
}

print("Listening for media keys...")
RunLoop.main.run()
