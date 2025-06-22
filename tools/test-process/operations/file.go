package operations

import (
	"fmt"
	"log"
	"os"
	"path/filepath"
	"time"
)

// Report is imported from main package (avoiding circular import by using interface)
type FileReport interface {
	GetConfig() Config
	IncrementSuccess()
	IncrementFailed()
	AddError(error)
	SetTotalOps(int)
}

type Config struct {
	Count    int
	Interval time.Duration
	Dir      string
	Verbose  bool
	Duration time.Duration
}

// ExecuteFileWrite performs file write operations
func ExecuteFileWrite(report FileReport) error {
	config := report.GetConfig()
	report.SetTotalOps(config.Count)
	
	if config.Verbose {
		log.Printf("ファイル書き込み操作開始: %d回、間隔 %v", config.Count, config.Interval)
	}

	for i := 0; i < config.Count; i++ {
		fileName := fmt.Sprintf("test_write_%d_%d.txt", os.Getpid(), i)
		filePath := filepath.Join(config.Dir, fileName)
		
		content := fmt.Sprintf("Test write operation %d\nTimestamp: %s\nProcess ID: %d\n", 
			i+1, time.Now().Format(time.RFC3339), os.Getpid())
		
		if config.Verbose {
			log.Printf("ファイル書き込み中: %s", filePath)
		}

		err := os.WriteFile(filePath, []byte(content), 0644)
		if err != nil {
			report.AddError(fmt.Errorf("ファイル書き込みエラー %s: %w", filePath, err))
			report.IncrementFailed()
		} else {
			report.IncrementSuccess()
			if config.Verbose {
				log.Printf("ファイル書き込み完了: %s", filePath)
			}
		}

		if i < config.Count-1 {
			time.Sleep(config.Interval)
		}
	}

	return nil
}

// ExecuteFileRead performs file read operations
func ExecuteFileRead(report FileReport) error {
	config := report.GetConfig()
	
	// First create some files to read
	tempFiles := make([]string, config.Count)
	for i := 0; i < config.Count; i++ {
		fileName := fmt.Sprintf("test_read_%d_%d.txt", os.Getpid(), i)
		filePath := filepath.Join(config.Dir, fileName)
		content := fmt.Sprintf("Test content for reading %d\nCreated: %s\n", 
			i+1, time.Now().Format(time.RFC3339))
		
		err := os.WriteFile(filePath, []byte(content), 0644)
		if err != nil {
			return fmt.Errorf("事前ファイル作成エラー: %w", err)
		}
		tempFiles[i] = filePath
	}

	report.SetTotalOps(config.Count)
	
	if config.Verbose {
		log.Printf("ファイル読み込み操作開始: %d回、間隔 %v", config.Count, config.Interval)
	}

	for i, filePath := range tempFiles {
		if config.Verbose {
			log.Printf("ファイル読み込み中: %s", filePath)
		}

		data, err := os.ReadFile(filePath)
		if err != nil {
			report.AddError(fmt.Errorf("ファイル読み込みエラー %s: %w", filePath, err))
			report.IncrementFailed()
		} else {
			report.IncrementSuccess()
			if config.Verbose {
				log.Printf("ファイル読み込み完了: %s (%d bytes)", filePath, len(data))
			}
		}

		// Clean up the file after reading
		os.Remove(filePath)

		if i < len(tempFiles)-1 {
			time.Sleep(config.Interval)
		}
	}

	return nil
}

// ExecuteFileDelete performs file delete operations
func ExecuteFileDelete(report FileReport) error {
	config := report.GetConfig()
	
	// First create some files to delete
	tempFiles := make([]string, config.Count)
	for i := 0; i < config.Count; i++ {
		fileName := fmt.Sprintf("test_delete_%d_%d.txt", os.Getpid(), i)
		filePath := filepath.Join(config.Dir, fileName)
		content := fmt.Sprintf("Test file for deletion %d\nCreated: %s\n", 
			i+1, time.Now().Format(time.RFC3339))
		
		err := os.WriteFile(filePath, []byte(content), 0644)
		if err != nil {
			return fmt.Errorf("事前ファイル作成エラー: %w", err)
		}
		tempFiles[i] = filePath
	}

	report.SetTotalOps(config.Count)
	
	if config.Verbose {
		log.Printf("ファイル削除操作開始: %d回、間隔 %v", config.Count, config.Interval)
	}

	for i, filePath := range tempFiles {
		if config.Verbose {
			log.Printf("ファイル削除中: %s", filePath)
		}

		err := os.Remove(filePath)
		if err != nil {
			report.AddError(fmt.Errorf("ファイル削除エラー %s: %w", filePath, err))
			report.IncrementFailed()
		} else {
			report.IncrementSuccess()
			if config.Verbose {
				log.Printf("ファイル削除完了: %s", filePath)
			}
		}

		if i < len(tempFiles)-1 {
			time.Sleep(config.Interval)
		}
	}

	return nil
}

// ExecuteFileRename performs file rename operations
func ExecuteFileRename(report FileReport) error {
	config := report.GetConfig()
	
	// First create some files to rename
	tempFiles := make([]string, config.Count)
	for i := 0; i < config.Count; i++ {
		fileName := fmt.Sprintf("test_rename_old_%d_%d.txt", os.Getpid(), i)
		filePath := filepath.Join(config.Dir, fileName)
		content := fmt.Sprintf("Test file for renaming %d\nCreated: %s\n", 
			i+1, time.Now().Format(time.RFC3339))
		
		err := os.WriteFile(filePath, []byte(content), 0644)
		if err != nil {
			return fmt.Errorf("事前ファイル作成エラー: %w", err)
		}
		tempFiles[i] = filePath
	}

	report.SetTotalOps(config.Count)
	
	if config.Verbose {
		log.Printf("ファイルリネーム操作開始: %d回、間隔 %v", config.Count, config.Interval)
	}

	for i, oldPath := range tempFiles {
		newFileName := fmt.Sprintf("test_rename_new_%d_%d.txt", os.Getpid(), i)
		newPath := filepath.Join(config.Dir, newFileName)
		
		if config.Verbose {
			log.Printf("ファイルリネーム中: %s -> %s", oldPath, newPath)
		}

		err := os.Rename(oldPath, newPath)
		if err != nil {
			report.AddError(fmt.Errorf("ファイルリネームエラー %s -> %s: %w", oldPath, newPath, err))
			report.IncrementFailed()
		} else {
			report.IncrementSuccess()
			if config.Verbose {
				log.Printf("ファイルリネーム完了: %s -> %s", oldPath, newPath)
			}
			// Clean up the renamed file
			os.Remove(newPath)
		}

		if i < len(tempFiles)-1 {
			time.Sleep(config.Interval)
		}
	}

	return nil
}

// ExecuteDirectoryOps performs directory operations
func ExecuteDirectoryOps(report FileReport) error {
	config := report.GetConfig()
	report.SetTotalOps(config.Count * 2) // Create + Delete
	
	if config.Verbose {
		log.Printf("ディレクトリ操作開始: %d回、間隔 %v", config.Count, config.Interval)
	}

	for i := 0; i < config.Count; i++ {
		dirName := fmt.Sprintf("test_dir_%d_%d", os.Getpid(), i)
		dirPath := filepath.Join(config.Dir, dirName)
		
		// Create directory
		if config.Verbose {
			log.Printf("ディレクトリ作成中: %s", dirPath)
		}

		err := os.Mkdir(dirPath, 0755)
		if err != nil {
			report.AddError(fmt.Errorf("ディレクトリ作成エラー %s: %w", dirPath, err))
			report.IncrementFailed()
		} else {
			report.IncrementSuccess()
			if config.Verbose {
				log.Printf("ディレクトリ作成完了: %s", dirPath)
			}
		}

		time.Sleep(config.Interval / 2)

		// Delete directory
		if config.Verbose {
			log.Printf("ディレクトリ削除中: %s", dirPath)
		}

		err = os.Remove(dirPath)
		if err != nil {
			report.AddError(fmt.Errorf("ディレクトリ削除エラー %s: %w", dirPath, err))
			report.IncrementFailed()
		} else {
			report.IncrementSuccess()
			if config.Verbose {
				log.Printf("ディレクトリ削除完了: %s", dirPath)
			}
		}

		if i < config.Count-1 {
			time.Sleep(config.Interval / 2)
		}
	}

	return nil
}

// ExecuteContinuous performs continuous file operations for specified duration
func ExecuteContinuous(report FileReport) error {
	config := report.GetConfig()
	
	if config.Duration <= 0 {
		return fmt.Errorf("継続実行時間が設定されていません")
	}
	
	if config.Verbose {
		log.Printf("継続ファイル操作開始: %v間継続、間隔 %v", config.Duration, config.Interval)
	}

	startTime := time.Now()
	endTime := startTime.Add(config.Duration)
	operationCount := 0

	// Start continuous operations
	for time.Now().Before(endTime) {
		// Perform a cycle of write -> read -> delete operations
		fileName := fmt.Sprintf("continuous_%d_%d.txt", os.Getpid(), operationCount)
		filePath := filepath.Join(config.Dir, fileName)
		
		content := fmt.Sprintf("Continuous operation %d\nTimestamp: %s\nProcess ID: %d\n", 
			operationCount+1, time.Now().Format(time.RFC3339), os.Getpid())
		
		if config.Verbose {
			log.Printf("継続操作 %d: ファイル作成 -> 読み込み -> 削除", operationCount+1)
		}

		// Write file
		err := os.WriteFile(filePath, []byte(content), 0644)
		if err != nil {
			report.AddError(fmt.Errorf("継続書き込みエラー %s: %w", filePath, err))
			report.IncrementFailed()
		} else {
			report.IncrementSuccess()
			
			// Read file
			if _, err := os.ReadFile(filePath); err != nil {
				report.AddError(fmt.Errorf("継続読み込みエラー %s: %w", filePath, err))
				report.IncrementFailed()
			} else {
				report.IncrementSuccess()
				
				// Delete file
				if err := os.Remove(filePath); err != nil {
					report.AddError(fmt.Errorf("継続削除エラー %s: %w", filePath, err))
					report.IncrementFailed()
				} else {
					report.IncrementSuccess()
				}
			}
		}

		operationCount++
		
		// Check if we should continue
		if time.Now().Add(config.Interval).After(endTime) {
			break
		}
		
		time.Sleep(config.Interval)
	}

	report.SetTotalOps(operationCount * 3) // write + read + delete
	
	actualDuration := time.Since(startTime)
	if config.Verbose {
		log.Printf("継続操作完了: %d回のサイクル、実行時間 %v", operationCount, actualDuration)
	}

	return nil
}