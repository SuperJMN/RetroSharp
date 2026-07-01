void Main() {
    Video.Init();
    Music.Asset(terminate, "music/terminate.gbapu");
    Music.Asset(blue_ocean, "music/blue_ocean_remix.uge");
    Audio.Init();
    Music.Play(terminate);

    bool onBlueOcean = false;
    loop {
        Video.WaitVBlank();
        Input.Poll();
        Audio.Update();
        if (Input.WasPressed(Button.Start)) {
            if (!onBlueOcean) {
                Music.Play(blue_ocean);
                onBlueOcean = true;
            } else {
                Music.Play(terminate);
                onBlueOcean = false;
            }
        }
    }
}
