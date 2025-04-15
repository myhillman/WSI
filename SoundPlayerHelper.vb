Imports System.Media
Imports System.Timers

''' <summary>
''' A helper class for playing WAV sound files with a specified duration limit.
''' This class provides functionality to play a sound, stop it after a given duration,
''' and check if a sound is currently playing.
''' </summary>
''' <remarks>
''' The SoundPlayerHelper class is designed to simplify sound playback in applications.
''' It uses the System.Media.SoundPlayer class for playing WAV files and a timer
''' to enforce a playback duration limit.
''' </remarks>
Public Class SoundPlayerHelper
    Private player As SoundPlayer
    Private playbackTimer As Timers.Timer
    Private IsPlaying As Boolean = False ' Tracks whether the sound is currently playing
    Public soundFileDir As String = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\..\..\Sounds\")  ' directory of sound files

    ''' <summary>
    ''' Plays a specified WAV file for a limited duration.
    ''' </summary>
    ''' <param name="filename">The name of the WAV file to play (located in the sound file directory).</param>
    ''' <param name="durationInMilliseconds">The duration to play the sound, in milliseconds.</param>
    ''' <remarks>
    ''' This method performs the following steps:
    ''' 1. Checks if a sound is already playing. If so, it exits without starting a new playback.
    ''' 2. Initializes the `SoundPlayer` instance with the specified WAV file from the sound file directory.
    ''' 3. Attempts to load the WAV file. If loading fails, an error message is displayed to the user.
    ''' 4. Starts playback of the WAV file and sets a flag (`IsPlaying`) to indicate that playback is active.
    ''' 5. Configures a timer to stop playback after the specified duration. The timer ensures that playback
    '''    is automatically stopped even if the sound file is longer than the specified duration.
    ''' 6. Handles any exceptions that occur during the loading of the WAV file to ensure the application remains stable.
    ''' 
    ''' This method is designed to provide controlled playback of WAV files, ensuring that only one sound
    ''' is played at a time and that playback is automatically stopped after the specified duration.
    ''' </remarks>

    Public Sub PlayWavWithLimit(filename As String, durationInMilliseconds As Integer)
        ' Check if already playing
        If IsPlaying Then
            Debug.WriteLine("SoundPlayer is already busy.")
            Return
        End If

        ' Initialize the SoundPlayer
        player = New SoundPlayer(IO.Path.Combine(soundFileDir, filename))

        ' Load the WAV file
        Try
            player.Load()
        Catch ex As Exception
            MessageBox.Show($"Error loading WAV file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ' Set the flag to indicate playback has started
        IsPlaying = True

        ' Play the sound
        player.Play()

        ' Set up the timer to stop playback
        playbackTimer = New Timers.Timer(durationInMilliseconds)
        AddHandler playbackTimer.Elapsed, AddressOf StopPlayback
        playbackTimer.AutoReset = False ' Ensure the timer runs only once
        playbackTimer.Start()
    End Sub

    ''' <summary>
    ''' Stops the playback and resets the playback state.
    ''' </summary>
    Private Sub StopPlayback(sender As Object, e As ElapsedEventArgs)
        ' Stop the sound
        player.Stop()

        ' Reset the flag
        IsPlaying = False

        ' Dispose of the timer
        playbackTimer.Stop()
        playbackTimer.Dispose()
    End Sub

    ''' <summary>
    ''' Checks if the SoundPlayer is currently playing.
    ''' </summary>
    ''' <returns>True if the SoundPlayer is playing, otherwise False.</returns>
    Public Function IsSoundPlaying() As Boolean
        Return IsPlaying
    End Function
    ''' <summary>
    ''' Retrieves a list of all WAV files in the sound file directory.
    ''' </summary>
    ''' <returns>A list of file names (without paths) for all WAV files in the directory.</returns>
    Public Function GetWavFiles() As List(Of String)
        Try
            ' Ensure the directory exists
            If Not IO.Directory.Exists(soundFileDir) Then
                Throw New IO.DirectoryNotFoundException($"Sound file directory not found: {soundFileDir}")
            End If

            ' Get all .wav files in the directory
            Dim wavFiles = IO.Directory.GetFiles(soundFileDir, "*.wav").
                       Select(Function(file) IO.Path.GetFileName(file)).
                       ToList()

            Return wavFiles
        Catch ex As Exception
            MessageBox.Show($"Error retrieving WAV files: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return New List(Of String)() ' Return an empty list on error
        End Try
    End Function
End Class
