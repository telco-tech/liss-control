# LiSS Control

For customers who want to manage our LiSS appliances by a desktop application we provide LiSS Control as a Proof of Concept.

This is a .NET Core console project that build portable applications.

If you wish to have an executable application file (exe) you need to run publish yourself, e.g.:

    dotnet publish -c Debug -r win10-x64

The application is intended to start by clicking on a desktop link.
Therefor a couple of arguments are prepared.
Each of them have to start with long option `--` followed by a key name and value assignment separated by `=` sign.

`--host=demo.liss.de` First we need the host name or IP address we want to connect to.

`--port=10443` For a device that is not running on default TCP port it is possible to switch to a different number.

`--user=demo` To proceed the authentication process a user name and password `--password=Demo4Liss` is needed.

`--operation=Poweroff` Next we tell which operation should perform on the target device.

`--thumbprint=B8FCD3F89D6F7504A870907BAE791A2166894B27` The connection is secured by TLS and needs to verify the server side offered certificate.
If your device uses a self signed certificate you must set the SHA-1 thumbprint here.
